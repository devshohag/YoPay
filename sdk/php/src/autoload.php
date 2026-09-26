<?php

declare(strict_types=1);

// For shops without Composer, which is most of them. require this one file and the
// three classes are available.
spl_autoload_register(static function (string $class): void {
    if (!str_starts_with($class, 'YoPay\\')) {
        return;
    }

    $file = __DIR__ . '/' . substr($class, 6) . '.php';

    if (is_file($file)) {
        require_once $file;
    }
});
