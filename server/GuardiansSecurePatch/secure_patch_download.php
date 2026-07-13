<?php
// U-CREW Guardians şifreli paket indirme uç noktası.
// Bu dosyayı API klasörüne koyun: /api/secure_patch_download.php

require_once __DIR__ . '/../includes/bootstrap.php';

function guardians_download_fail($message, $code) {
    if (!headers_sent()) {
        http_response_code($code);
        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-store, no-cache, must-revalidate, max-age=0');
    }

    echo json_encode(array(
        'status' => 'error',
        'message' => $message
    ), JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    exit;
}

function guardians_resolve_patch_path($storedPath) {
    $storedPath = trim((string)$storedPath);
    if ($storedPath === '') return false;

    $isWindowsAbsolute = preg_match('/^[a-zA-Z]:[\\\\\/]/', $storedPath) === 1;
    $isUnixAbsolute = substr($storedPath, 0, 1) === '/';

    if ($isWindowsAbsolute || $isUnixAbsolute) {
        $candidate = $storedPath;
    } else {
        $candidate = dirname(__DIR__) . '/' . ltrim($storedPath, '/\\');
    }

    $real = realpath($candidate);
    if ($real === false || !is_file($real)) return false;

    // Göreli yollar yalnızca uygulama kökü içinden sunulabilir.
    if (!$isWindowsAbsolute && !$isUnixAbsolute) {
        $root = realpath(dirname(__DIR__));
        if ($root === false) return false;

        $rootNormalized = rtrim(str_replace('\\', '/', $root), '/') . '/';
        $realNormalized = str_replace('\\', '/', $real);

        if (strpos($realNormalized, $rootNormalized) !== 0) {
            return false;
        }
    }

    return $real;
}

$ticketToken = isset($_GET['ticket']) ? trim((string)$_GET['ticket']) : '';
$hwid = isset($_GET['hwid']) ? trim((string)$_GET['hwid']) : '';

if ($ticketToken === '' || strlen($ticketToken) > 128) {
    guardians_download_fail('İndirme bileti geçersiz.', 400);
}

if ($hwid === '' || strlen($hwid) > 128) {
    guardians_download_fail('Cihaz kimliği geçersiz.', 400);
}

try {
    $pdo = DB::pdo();

    $query = $pdo->prepare(
        "SELECT t.id AS ticket_id,
                t.hwid,
                t.expires_at,
                t.used_at,
                f.file_path,
                f.file_name,
                f.file_size,
                f.sha256,
                f.status
         FROM secure_patch_tickets t
         INNER JOIN secure_patch_files f ON f.id=t.patch_id
         WHERE t.token=?
         LIMIT 1"
    );
    $query->execute(array($ticketToken));
    $row = $query->fetch(PDO::FETCH_ASSOC);

    if (!$row) {
        guardians_download_fail('İndirme bileti bulunamadı.', 404);
    }

    if (!hash_equals((string)$row['hwid'], $hwid)) {
        guardians_download_fail('İndirme bileti bu cihaza ait değil.', 403);
    }

    if (strtotime((string)$row['expires_at']) < time()) {
        guardians_download_fail('İndirme biletinin süresi doldu.', 410);
    }

    if ((string)$row['status'] !== 'active') {
        guardians_download_fail('Yama paketi etkin değil.', 403);
    }

    if (!empty($row['used_at'])) {
        guardians_download_fail('İndirme bileti daha önce kullanılmış.', 410);
    }

    $filePath = guardians_resolve_patch_path($row['file_path']);
    if ($filePath === false) {
        error_log('Guardians secure patch file missing: ' . (string)$row['file_path']);
        guardians_download_fail('Şifreli yama dosyası sunucuda bulunamadı.', 404);
    }

    $actualSize = filesize($filePath);
    if ($actualSize === false || $actualSize <= 0) {
        guardians_download_fail('Şifreli yama dosyası boş.', 500);
    }

    $safeName = !empty($row['file_name'])
        ? basename((string)$row['file_name'])
        : 'guardians_patch.ucrew';

    ignore_user_abort(true);
    @set_time_limit(0);

    while (ob_get_level() > 0) {
        @ob_end_clean();
    }

    header('Content-Type: application/octet-stream');
    header('Content-Disposition: attachment; filename="' . addslashes($safeName) . '"');
    header('Content-Length: ' . (string)$actualSize);
    header('Cache-Control: no-store, no-cache, must-revalidate, max-age=0');
    header('Pragma: no-cache');
    header('Expires: 0');
    header('X-Content-Type-Options: nosniff');
    header('X-UCREW-SHA256: ' . strtolower((string)$row['sha256']));

    $handle = fopen($filePath, 'rb');
    if ($handle === false) {
        guardians_download_fail('Şifreli yama dosyası açılamadı.', 500);
    }

    $success = true;

    while (!feof($handle)) {
        $chunk = fread($handle, 1024 * 1024);
        if ($chunk === false) {
            $success = false;
            break;
        }

        echo $chunk;
        flush();
    }

    fclose($handle);

    if ($success) {
        $mark = $pdo->prepare(
            'UPDATE secure_patch_tickets SET used_at=NOW() WHERE id=? AND used_at IS NULL'
        );
        $mark->execute(array((int)$row['ticket_id']));
    }

    exit;
} catch (Throwable $error) {
    error_log('Guardians secure patch download: ' . $error->getMessage());
    guardians_download_fail('Şifreli yama indirilemedi.', 500);
}
