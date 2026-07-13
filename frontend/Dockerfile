FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

# Os arquivos estáticos são servidos pelo Caddy via volume compartilhado (ver docker-compose).
FROM alpine:3
COPY --from=build /app/dist /dist
CMD ["sh", "-c", "cp -r /dist/* /srv/ && sleep infinity"]
