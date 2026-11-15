# syntax=docker/dockerfile:1
# Build from benchmarks/frameworks: docker build -f laravel/Dockerfile [--target dev] .
# The repository holds only what differs from the Laravel skeleton (pinned below); composer.lock pins every PHP package.
FROM node:24.22.3-bookworm-slim AS node

FROM node AS client
WORKDIR /app
COPY laravel/package.json laravel/package-lock.json ./
RUN npm ci
COPY laravel/vite.config.js ./
COPY laravel/resources resources
COPY shared/js resources/js/shared
RUN npm run build

FROM dunglas/frankenphp:1.13.5-php8.5-trixie AS php
RUN install-php-extensions pdo_pgsql redis-6.3.0 pcntl zip && cp "$PHP_INI_DIR/php.ini-production" "$PHP_INI_DIR/php.ini"
COPY --from=composer:2.11.2 /usr/bin/composer /usr/bin/composer
# Node runs Inertia's SSR server (php artisan inertia:start-ssr); the SSR bundle inlines its dependencies.
COPY --from=node /usr/local/bin/node /usr/local/bin/node
WORKDIR /app
RUN rm -rf /app/* && composer create-project laravel/laravel=v13.11.0 /tmp/skeleton --no-install --no-scripts --quiet && cp -a /tmp/skeleton/. /app/ && rm -rf /tmp/skeleton
COPY laravel/composer.json laravel/composer.lock ./

FROM php AS runtime
RUN composer install --no-dev --no-scripts --no-autoloader --quiet
COPY laravel/ ./
RUN cp .env.bench .env && composer dump-autoload --optimize --no-dev --quiet && php artisan package:discover --quiet && php artisan key:generate --force --quiet
COPY --from=client /app/public/build public/build
COPY --from=client /app/bootstrap/ssr bootstrap/ssr
EXPOSE 8080
ENTRYPOINT ["./start.sh"]

# Dev-server cold start (run.sh --dev): php artisan serve and Vite side by side.
FROM php AS dev
RUN composer install --no-scripts --no-autoloader --quiet
COPY laravel/ ./
COPY shared/js resources/js/shared
COPY --from=client /app/node_modules node_modules
RUN cp .env.bench .env && sed -i 's/^APP_ENV=.*/APP_ENV=local/; s/^APP_DEBUG=.*/APP_DEBUG=true/' .env && composer dump-autoload --quiet && php artisan package:discover --quiet && php artisan key:generate --force --quiet
EXPOSE 8080
CMD ["sh", "-c", "node node_modules/vite/bin/vite.js & exec php artisan serve --host=0.0.0.0 --port=8080"]
