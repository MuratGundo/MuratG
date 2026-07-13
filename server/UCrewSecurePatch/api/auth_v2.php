<?php
// U-CREW API auth helper - PHP 5.6+ uyumlu
if (!defined('UCREW_APP')) { define('UCREW_APP', true); }

if (session_status() === PHP_SESSION_NONE) {
    session_start();
}

if (!function_exists('ucrew_api_response')) {
    function ucrew_api_response($data, $code = 200) {
        while (ob_get_level() > 0) { @ob_end_clean(); }
        if (!headers_sent()) {
            http_response_code($code);
            header('Content-Type: application/json; charset=utf-8');
            header('Access-Control-Allow-Origin: *');
            header('Access-Control-Allow-Headers: Authorization, Content-Type');
            header('Access-Control-Allow-Methods: GET, POST, OPTIONS');
        }
        echo json_encode($data, JSON_UNESCAPED_UNICODE);
        exit;
    }
}

if (!function_exists('ucrew_token_secret')) {
    function ucrew_token_secret() {
        if (defined('APP_KEY')) return APP_KEY;
        if (defined('APP_SECRET')) return APP_SECRET;
        if (defined('DB_PASS')) return DB_PASS;
        return 'ucrew_patch_v3_token_secret_2026';
    }
}

if (!function_exists('ucrew_get_bearer_token')) {
    function ucrew_get_bearer_token() {
        $auth = '';

        if (function_exists('getallheaders')) {
            $headers = getallheaders();
            if (isset($headers['Authorization'])) $auth = $headers['Authorization'];
            elseif (isset($headers['authorization'])) $auth = $headers['authorization'];
        }

        if ($auth === '' && isset($_SERVER['HTTP_AUTHORIZATION'])) $auth = $_SERVER['HTTP_AUTHORIZATION'];
        if ($auth === '' && isset($_SERVER['REDIRECT_HTTP_AUTHORIZATION'])) $auth = $_SERVER['REDIRECT_HTTP_AUTHORIZATION'];
        if ($auth === '' && isset($_SERVER['Authorization'])) $auth = $_SERVER['Authorization'];

        if ($auth !== '' && preg_match('/Bearer\s+(.+)/i', $auth, $m)) {
            return trim($m[1]);
        }

        if (isset($_POST['token']) && trim($_POST['token']) !== '') return trim($_POST['token']);
        if (isset($_GET['token']) && trim($_GET['token']) !== '') return trim($_GET['token']);

        return '';
    }
}

if (!function_exists('ucrew_user_by_id')) {
    function ucrew_user_by_id($uid) {
        try {
            $st = DB::pdo()->prepare('SELECT * FROM users WHERE id=? LIMIT 1');
            $st->execute(array((int)$uid));
            $u = $st->fetch(PDO::FETCH_ASSOC);
            return $u ? $u : false;
        } catch (Exception $e) {
            return false;
        }
    }
}

if (!function_exists('ucrew_api_make_token')) {
    function ucrew_api_make_token($user_id) {
        $user = ucrew_user_by_id((int)$user_id);
        $hash = '';
        if ($user) {
            if (isset($user['password_hash'])) $hash = (string)$user['password_hash'];
            elseif (isset($user['password'])) $hash = (string)$user['password'];
        }

        $uid = (int)$user_id;
        $sig = hash('sha256', $uid . '|' . $hash . '|' . ucrew_token_secret());

        $_SESSION['user_id'] = $uid;
        $_SESSION['admin_id'] = $uid;

        return 'ucrew_v2_' . $uid . '_' . $sig;
    }
}

if (!function_exists('ucrew_user_from_token')) {
    function ucrew_user_from_token($token) {
        $token = trim((string)$token);
        if ($token === '') return false;

        if (!preg_match('/^ucrew_v2_(\d+)_([a-f0-9]{64})$/i', $token, $m)) {
            return false;
        }

        $uid = (int)$m[1];
        $sig = strtolower($m[2]);

        $user = ucrew_user_by_id($uid);
        if (!$user) return false;

        $hash = '';
        if (isset($user['password_hash'])) $hash = (string)$user['password_hash'];
        elseif (isset($user['password'])) $hash = (string)$user['password'];

        $expected = hash('sha256', $uid . '|' . $hash . '|' . ucrew_token_secret());

        if (!function_exists('hash_equals')) {
            if ($expected !== $sig) return false;
        } else {
            if (!hash_equals($expected, $sig)) return false;
        }

        $_SESSION['user_id'] = $uid;
        $_SESSION['admin_id'] = $uid;

        return $user;
    }
}

if (!function_exists('bearer_user')) {
    function bearer_user() {
        $token = ucrew_get_bearer_token();
        if ($token !== '') {
            $u = ucrew_user_from_token($token);
            if ($u) return $u;
        }

        if (isset($_SESSION['user_id']) && (int)$_SESSION['user_id'] > 0) {
            $u = ucrew_user_by_id((int)$_SESSION['user_id']);
            if ($u) return $u;
        }

        if (isset($_SESSION['admin_id']) && (int)$_SESSION['admin_id'] > 0) {
            $u = ucrew_user_by_id((int)$_SESSION['admin_id']);
            if ($u) return $u;
        }

        return false;
    }
}

if (!function_exists('require_login')) {
    function require_login() {
        $user = bearer_user();

        if (!$user) {
            ucrew_api_response(array(
                'status' => 'error',
                'message' => 'Oturum bulunamadı. Tekrar giriş yap.'
            ), 401);
        }

        return $user;
    }
}