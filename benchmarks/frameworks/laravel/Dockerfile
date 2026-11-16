# syntax=docker/dockerfile:1
# Runtime images on FrankenPHP's Alpine variant. run.sh builds the Vite client and SSR bundles on the host first (public/build,
# bootstrap/ssr); PHP has no runtime on the host, so `composer install` runs in the vendor stage here, with Composer pinned.
# The repository holds only what differs from the Laravel skeleton (pinned below); composer.lock pins every PHP package.
# Build from this folder: docker build --target ssr|spa .  Only the SSR image carries Node (Alpine's musl build), for Inertia's SSR server.
FROM dunglas/frankenphp:1.13.5-php8.5-alpine AS php
RUN install-php-extensions pdo_pgsql redis-6.3.0 pcntl && cp "$PHP_INI_DIR/php.ini-production" "$PHP_INI_DIR/php.ini"
WORKDIR /app

FROM php AS vendor
RUN install-php-extensions zip
COPY --from=composer:2.11.2 /usr/bin/composer /usr/bin/composer
RUN rm -rf /app/* && composer create-project laravel/laravel=v13.11.0 /tmp/skeleton --no-install --no-scripts --quiet && cp -a /tmp/skeleton/. /app/ && rm -rf /tmp/skeleton
COPY composer.json composer.lock ./
RUN composer install --no-dev --no-scripts --no-autoloader --quiet
COPY . ./
RUN cp .env.bench .env && composer dump-autoload --optimize --no-dev --quiet && php artisan package:discover --quiet && php artisan key:generate --force --quiet

FROM php AS spa
COPY --from=vendor /app /app
EXPOSE 8080
ENTRYPOINT ["./start.sh"]

FROM spa AS ssr
RUN apk add --no-cache nodejs=24.22.3-r0
