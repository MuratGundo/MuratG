<?php
// U-CREW Launcher login endpoint - JSON ve form POST destekler.
ob_start();
require_once __DIR__ . '/../includes/bootstrap.php';
require_once __DIR__ . '/auth_v2.php';

function ucrew_login_json_body() {
    $raw = file_get_contents('php://input');
    $json = json_decode($raw, true);
    return is_array($json) ? $json : array();
}

function ucrew_login_col_exists($table, $col) {
    try {
        $db = DB::pdo()->query('SELECT DATABASE()')->fetchColumn();
        $st = DB::pdo()->prepare('SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=? AND TABLE_NAME=? AND COLUMN_NAME=?');
        $st->execute(array($db, $table, $col));
        return ((int)$st->fetchColumn()) > 0;
    } catch (Exception $e) {
        return false;
    }
}

$in = array_merge($_POST, ucrew_login_json_body());
$email = '';
if (isset($in['email'])) $email = trim($in['email']);
elseif (isset($in['username'])) $email = trim($in['username']);

$password = isset($in['password']) ? (string)$in['password'] : '';

if ($email === '' || $password === '') {
    ucrew_api_response(array('status'=>'error','message'=>'E-posta ve şifre gerekli.'), 400);
}

try {
    DB::pdo();

    $has_status = ucrew_login_col_exists('users', 'status');
    $has_is_banned = ucrew_login_col_exists('users', 'is_banned');
    $has_display_name = ucrew_login_col_exists('users', 'display_name');
    $has_name = ucrew_login_col_exists('users', 'name');
    $has_username = ucrew_login_col_exists('users', 'username');
    $has_password_hash = ucrew_login_col_exists('users', 'password_hash');
    $has_password = ucrew_login_col_exists('users', 'password');

    $st = DB::pdo()->prepare('SELECT * FROM users WHERE LOWER(email)=LOWER(?) LIMIT 1');
    $st->execute(array($email));
    $user = $st->fetch(PDO::FETCH_ASSOC);

    if (!$user && $has_username) {
        $st = DB::pdo()->prepare('SELECT * FROM users WHERE LOWER(username)=LOWER(?) LIMIT 1');
        $st->execute(array($email));
        $user = $st->fetch(PDO::FETCH_ASSOC);
    }

    if (!$user) {
        ucrew_api_response(array('status'=>'error','message'=>'Kullanıcı bulunamadı.'), 401);
    }

    if ($has_status && isset($user['status']) && $user['status'] !== '' && $user['status'] !== 'active' && $user['status'] !== '1') {
        ucrew_api_response(array('status'=>'error','message'=>'Kullanıcı aktif değil.'), 403);
    }

    if ($has_is_banned && !empty($user['is_banned'])) {
        ucrew_api_response(array('status'=>'error','message'=>'Kullanıcı banlı.'), 403);
    }

    $hash = '';
    if ($has_password_hash && !empty($user['password_hash'])) $hash = $user['password_hash'];
    elseif ($has_password && !empty($user['password'])) $hash = $user['password'];

    $ok = false;
    if ($hash !== '' && function_exists('password_verify') && password_verify($password, $hash)) $ok = true;
    if (!$ok && $hash !== '' && strlen($hash) === 32 && md5($password) === $hash) $ok = true;
    if (!$ok && $hash !== '' && strlen($hash) === 40 && sha1($password) === $hash) $ok = true;
    if (!$ok && $hash !== '' && $hash === $password) $ok = true;

    if (!$ok) {
        ucrew_api_response(array('status'=>'error','message'=>'Şifre hatalı.'), 401);
    }

    $uid = (int)$user['id'];
    $_SESSION['user_id'] = $uid;
    $_SESSION['admin_id'] = $uid;

    $token = ucrew_api_make_token($uid);

    $name = '';
    if ($has_display_name && !empty($user['display_name'])) $name = $user['display_name'];
    elseif ($has_name && !empty($user['name'])) $name = $user['name'];
    elseif ($has_username && !empty($user['username'])) $name = $user['username'];
    else $name = isset($user['email']) ? $user['email'] : $email;

    ucrew_api_response(array(
        'status' => 'ok',
        'message' => 'Giriş başarılı.',
        'token' => $token,
        'user' => array(
            'id' => $uid,
            'email' => isset($user['email']) ? $user['email'] : $email,
            'name' => $name
        )
    ));
} catch (Exception $e) {
    ucrew_api_response(array('status'=>'error','message'=>'Login hata: '.$e->getMessage()), 500);
}