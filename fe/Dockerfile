# syntax=docker/dockerfile:1
FROM node:22-alpine AS build
WORKDIR /app

COPY fe/package*.json ./
RUN npm install --no-audit --no-fund

COPY fe/ ./
RUN npm run build

FROM nginx:alpine AS runtime
COPY fe/nginx.conf /etc/nginx/nginx.conf
COPY --from=build /app/dist/fe/browser /usr/share/nginx/html

RUN addgroup -g 10001 -S appuser \
    && adduser -u 10001 -S appuser -G appuser \
    && chown -R appuser:appuser /usr/share/nginx/html /var/cache/nginx /var/log/nginx \
    && touch /tmp/nginx.pid && chown appuser:appuser /tmp/nginx.pid

USER appuser
EXPOSE 4200
CMD ["nginx", "-g", "daemon off;"]

