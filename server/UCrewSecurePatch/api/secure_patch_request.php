<?php
// U-CREW genel güvenli yama bileti üretimi.
// Tüm oyunlar aynı uç noktayı kullanır; oyun seçimi slug ile yapılır.

ob_start();
require_once __DIR__ . '/../../includes/bootstrap.php';
require_once __DIR__ . '/auth_v2.php';

function ucrew_secure_out($data, $code = 200) {
    while (ob_get_level() > 0) { @ob_end_clean(); }

    if (!headers_sent()) {
        http_response_code($code);
        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-store, no-cache, must-revalidate, max-age=0');
        header('Pragma: no-cache');
        header('Access-Control-Allow-Origin: *');
        header('Access-Control-Allow-Headers: Authorization, Content-Type');
        header('Access-Control-Allow-Methods: POST, OPTIONS');
    }

    echo json_encode($data, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    exit;
}

if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
    ucrew_secure_out(array('status' => 'ok'));
}

if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    ucrew_secure_out(array(
        'status' => 'error',
        'message' => 'Yalnızca POST isteği kabul edilir.'
    ), 405);
}

function ucrew_secure_token() {
    if (!empty($_POST['token'])) {
        return trim((string)$_POST['token']);
    }

    $header = '';

    if (!empty($_SERVER['HTTP_AUTHORIZATION'])) {
        $header = trim((string)$_SERVER['HTTP_AUTHORIZATION']);
    } elseif (function_exists('getallheaders')) {
        $headers = getallheaders();
        if (!empty($headers['Authorization'])) {
            $header = trim((string)$headers['Authorization']);
        } elseif (!empty($headers['authorization'])) {
            $header = trim((string)$headers['authorization']);
        }
    }

    if (preg_match('/^Bearer\s+(.+)$/i', $header, $match)) {
        return trim((string)$match[1]);
    }

    return '';
}

function ucrew_secure_user() {
    $token = ucrew_secure_token();

    if ($token !== '' && function_exists('ucrew_user_from_token')) {
        $user = ucrew_user_from_token($token);
        if ($user) return $user;
    }

    if (function_exists('bearer_user')) {
        $user = bearer_user();
        if ($user) return $user;
    }

    return false;
}

function ucrew_decode_profile($json) {
    $json = trim((string)$json);
    if ($json === '') return new stdClass();

    $decoded = json_decode($json, true);
    if (!is_array($decoded)) return new stdClass();

    return $decoded;
}

try {
    $user = ucrew_secure_user();
    $uid = ($user && isset($user['id'])) ? (int)$user['id'] : 0;

    if ($uid <= 0) {
        ucrew_secure_out(array(
            'status' => 'error',
            'message' => 'Oturum geçersiz. U-CREW Launcher üzerinden tekrar giriş yapın.'
        ), 401);
    }

    $slug = isset($_POST['slug']) ? trim((string)$_POST['slug']) : '';
    $hwid = isset($_POST['hwid']) ? trim((string)$_POST['hwid']) : '';
    $channel = isset($_POST['channel']) ? trim((string)$_POST['channel']) : 'stable';

    if ($slug === '' || !preg_match('/^[a-z0-9][a-z0-9._-]{1,99}$/i', $slug)) {
        ucrew_secure_out(array(
            'status' => 'error',
            'message' => 'Oyun kodu geçersiz.'
        ), 400);
    }

    if ($hwid === '' || strlen($hwid) > 128) {
        ucrew_secure_out(array(
            'status' => 'error',
            'message' => 'Cihaz kimliği geçersiz.'
        ), 400);
    }

    if ($channel === '' || !preg_match('/^[a-z0-9._-]{1,32}$/i', $channel)) {
        $channel = 'stable';
    }

    $pdo = DB::pdo();

    $gameQuery = $pdo->prepare(
        'SELECT id, slug, title FROM games WHERE LOWER(slug)=LOWER(?) LIMIT 1'
    );
    $gameQuery->execute(array($slug));
    $game = $gameQuery->fetch(PDO::FETCH_ASSOC);

    if (!$game) {
        ucrew_secure_out(array(
            'status' => 'error',
            'message' => 'Oyun U-CREW panelinde bulunamadı.'
        ), 404);
    }

    $licenseQuery = $pdo->prepare(
        "SELECT id
         FROM licenses
         WHERE user_id=?
           AND game_id=?
           AND status='active'
           AND (expires_at IS NULL OR expires_at > NOW())
         LIMIT 1"
    );
    $licenseQuery->execute(array($uid, (int)$game['id']));

    if (!$licenseQuery->fetch(PDO::FETCH_ASSOC)) {
        ucrew_secure_out(array(
            'status' => 'error',
            'message' => 'Bu oyun için aktif U-CREW lisansı bulunamadı.'
        ), 403);
    }

    $patchQuery = $pdo->prepare(
        "SELECT id, version, channel, package_format,
                file_name, file_size, sha256,
                key_base64, iv_base64, runtime_profile_json
         FROM secure_patch_files
         WHERE game_id=?
           AND channel=?
           AND status='active'
         ORDER BY updated_at DESC, created_at DESC, id DESC
         LIMIT 1"
    );
    $patchQuery->execute(array((int)$game['id'], $channel));
    $patch = $patchQuery->fetch(PDO::FETCH_ASSOC);

    if (!$patch) {
        ucrew_secure_out(array(
            'status' => 'error',
            'message' => 'Bu oyun ve kanal için etkin güvenli yama paketi bulunamadı.'
        ), 404);
    }

    $ticket = bin2hex(random_bytes(32));
    $expiresAt = date('Y-m-d H:i:s', time() + 600);

    $pdo->beginTransaction();

    $pdo->prepare(
        'DELETE FROM secure_patch_tickets WHERE expires_at < DATE_SUB(NOW(), INTERVAL 1 DAY)'
    )->execute();

    $insert = $pdo->prepare(
        'INSERT INTO secure_patch_tickets
         (user_id, game_id, patch_id, token, hwid, expires_at, used_at, created_at)
         VALUES (?, ?, ?, ?, ?, ?, NULL, NOW())'
    );
    $insert->execute(array(
        $uid,
        (int)$game['id'],
        (int)$patch['id'],
        $ticket,
        $hwid,
        $expiresAt
    ));

    $pdo->commit();

    $downloadUrl = 'https://api.u-crew.net/api/secure_patch_download.php'
        . '?ticket=' . rawurlencode($ticket)
        . '&hwid=' . rawurlencode($hwid);

    ucrew_secure_out(array(
        'status' => 'ok',
        'message' => 'Güvenli yama izni verildi.',
        'data' => array(
            'game_id' => (int)$game['id'],
            'game_slug' => (string)$game['slug'],
            'game_title' => isset($game['title']) ? (string)$game['title'] : (string)$game['slug'],
            'version' => (string)$patch['version'],
            'channel' => (string)$patch['channel'],
            'package_format' => (string)$patch['package_format'],
            'download_url' => $downloadUrl,
            'key_base64' => (string)$patch['key_base64'],
            'iv_base64' => (string)$patch['iv_base64'],
            'sha256' => strtolower((string)$patch['sha256']),
            'file_size' => (int)$patch['file_size'],
            'runtime_profile' => ucrew_decode_profile($patch['runtime_profile_json']),
            'expires_at' => $expiresAt
        )
    ));
} catch (Throwable $error) {
    if (isset($pdo) && $pdo instanceof PDO && $pdo->inTransaction()) {
        $pdo->rollBack();
    }

    error_log('U-CREW secure patch request: ' . $error->getMessage());

    ucrew_secure_out(array(
        'status' => 'error',
        'message' => 'Güvenli yama izni oluşturulamadı.'
    ), 500);
}
