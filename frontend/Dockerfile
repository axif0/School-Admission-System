# Build stage
FROM node:20-alpine AS build
WORKDIR /app

COPY package*.json ./
RUN npm ci

COPY . .
RUN npm run build

# Test stage
FROM build AS test
WORKDIR /app
CMD ["npm", "test"]

# Production unprivileged nginx stage
FROM nginxinc/nginx-unprivileged:alpine AS final

COPY nginx/default.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
COPY docker-entrypoint.d/40-runtime-config.sh /docker-entrypoint.d/40-runtime-config.sh

EXPOSE 8080

CMD ["nginx", "-g", "daemon off;"]
