# Boiler Telemetry — деплой одной командой.
#
# Один раз на свежей машине:   ./bootstrap.sh   (ставит docker/minikube/helm/kubectl)
# Поднять всё:                  make up
# Статус:                       make status
# Открыть UI:                   make ports          (URL'ы напечатает в конце)
# Погасить:                     make down

SHELL          := /bin/bash
NAMESPACE      := boiler
HELM_RELEASE   := boiler
EXTERNAL_HOST  := host.minikube.internal
PF_PID_FILE    := /tmp/boiler-pf.pids
DB_COMPOSE     := infra/databases/docker-compose.yml
CHART          := helm/boiler-telemetry

IMAGES         := app-boiler-telemetry-api app-anomaly-service app-notification-worker

.PHONY: help up down status ports ports-stop logs build install reload-images \
        databases minikube reset-grafana clean backup-now restore-postgres list-backups

help:
	@echo "Цели:"
	@echo "  make up               — полный деплой (БД в Docker + minikube + сборка + helm + port-forward'ы)"
	@echo "  make down             — снести port-forward'ы, helm-релиз, namespace, БД (volumes остаются)"
	@echo "  make status           — статус подов, HPA, prometheus targets"
	@echo "  make ports            — поднять port-forward'ы и распечатать URL"
	@echo "  make ports-stop       — прибить port-forward'ы"
	@echo "  make logs SVC=api     — логи сервиса (api / anomaly-service / notification-worker)"
	@echo "  make build            — пересобрать docker-образы и залить в minikube"
	@echo "  make install          — helm upgrade --install (без сборки)"
	@echo "  make reload-images    — пересобрать + перезалить образы + рестарт деплоев"
	@echo "  make reset-grafana    — сбросить пароль Grafana к admin/admin"
	@echo "  make backup-now       — сделать бэкап Postgres+Influx прямо сейчас"
	@echo "  make list-backups     — показать существующие бэкапы"
	@echo "  make restore-postgres FILE=...  — восстановить Postgres из дампа"
	@echo "  make clean            — make down + minikube delete + volumes БД"

# ── Полный деплой ────────────────────────────────────────────────────────────
up: databases minikube build install ports
	@echo ""
	@echo "✓ Готово. Открывай UI по URL'ам выше."

# ── Базы данных в Docker (вне k8s) ───────────────────────────────────────────
databases:
	@echo "==> Поднимаю Postgres + InfluxDB в Docker..."
	docker compose -f $(DB_COMPOSE) up -d
	@echo "==> Жду healthcheck..."
	@for i in $$(seq 1 30); do \
	    pg=$$(docker inspect --format '{{.State.Health.Status}}' boiler-postgres 2>/dev/null); \
	    ix=$$(docker inspect --format '{{.State.Health.Status}}' boiler-influxdb 2>/dev/null); \
	    [ "$$pg" = "healthy" ] && [ "$$ix" = "healthy" ] && echo "  ok" && break; \
	    echo "  postgres=$$pg influx=$$ix"; sleep 3; \
	done

# ── minikube ─────────────────────────────────────────────────────────────────
minikube:
	@if ! minikube status >/dev/null 2>&1; then \
	    echo "==> Запускаю minikube..."; \
	    minikube start --cpus=4 --memory=6144 --driver=docker --addons=metrics-server; \
	else \
	    echo "==> minikube уже запущен"; \
	fi

# ── Сборка образов и заливка в minikube ──────────────────────────────────────
build:
	@echo "==> Сборка docker-образов..."
	docker build -t app-boiler-telemetry-api:latest -f app/src/BoilerTelemetry.Api/Dockerfile               app
	docker build -t app-anomaly-service:latest      -f app/src/BoilerTelemetry.AnomalyService/Dockerfile    app
	docker build -t app-notification-worker:latest  -f app/src/BoilerTelemetry.NotificationWorker/Dockerfile app
	@echo "==> Заливаю в minikube..."
	@for img in $(IMAGES); do \
	    minikube ssh "docker rmi -f docker.io/library/$$img:latest" >/dev/null 2>&1 || true; \
	    minikube image load $$img:latest; \
	done

# ── Helm install ─────────────────────────────────────────────────────────────
install:
	@echo "==> helm upgrade --install $(HELM_RELEASE)..."
	helm upgrade --install $(HELM_RELEASE) $(CHART) \
	    --namespace $(NAMESPACE) --create-namespace \
	    --set externalHost=$(EXTERNAL_HOST) \
	    --timeout 8m
	@echo "==> Жду пока поды поднимутся..."
	kubectl -n $(NAMESPACE) rollout status deploy/boiler-api               --timeout=4m
	kubectl -n $(NAMESPACE) rollout status deploy/boiler-anomaly-service   --timeout=4m
	kubectl -n $(NAMESPACE) rollout status deploy/boiler-notification-worker --timeout=4m

# Пересобрать только приложения и перезапустить (без полного помешательства)
reload-images: build
	kubectl -n $(NAMESPACE) rollout restart deploy/boiler-api deploy/boiler-anomaly-service deploy/boiler-notification-worker
	kubectl -n $(NAMESPACE) rollout status  deploy/boiler-api               --timeout=4m
	kubectl -n $(NAMESPACE) rollout status  deploy/boiler-anomaly-service   --timeout=4m
	kubectl -n $(NAMESPACE) rollout status  deploy/boiler-notification-worker --timeout=4m

# ── Port-forward'ы для UI ────────────────────────────────────────────────────
ports: ports-stop
	@echo "==> Поднимаю port-forward'ы..."
	@: > $(PF_PID_FILE)
	@kubectl port-forward -n $(NAMESPACE) svc/boiler-nginx          18080:8080  >/dev/null 2>&1 & echo $$! >> $(PF_PID_FILE)
	@kubectl port-forward -n $(NAMESPACE) svc/opensearch-dashboards 5601:5601   >/dev/null 2>&1 & echo $$! >> $(PF_PID_FILE)
	@kubectl port-forward -n $(NAMESPACE) svc/grafana               3000:3000   >/dev/null 2>&1 & echo $$! >> $(PF_PID_FILE)
	@kubectl port-forward -n $(NAMESPACE) svc/jaeger-ui             16686:16686 >/dev/null 2>&1 & echo $$! >> $(PF_PID_FILE)
	@kubectl port-forward -n $(NAMESPACE) svc/kafka-ui              8085:8080   >/dev/null 2>&1 & echo $$! >> $(PF_PID_FILE)
	@kubectl port-forward -n $(NAMESPACE) svc/prometheus            9090:9090   >/dev/null 2>&1 & echo $$! >> $(PF_PID_FILE)
	@sleep 4
	@echo ""
	@echo "Открывай в браузере:"
	@echo "  API (через nginx)        http://localhost:18080"
	@echo "  Grafana                  http://localhost:3000     (admin / admin)"
	@echo "  OpenSearch Dashboards    http://localhost:5601"
	@echo "  Jaeger UI                http://localhost:16686"
	@echo "  Kafka UI                 http://localhost:8085"
	@echo "  Prometheus               http://localhost:9090"
	@echo ""
	@echo "Базы (Docker, не в k8s):"
	@echo "  InfluxDB UI              http://localhost:28086    (admin / adminpassword)"
	@echo "  Postgres                 localhost:25432           (postgres / postgres / boiler_telemetry)"

ports-stop:
	@if [ -f $(PF_PID_FILE) ]; then \
	    xargs -a $(PF_PID_FILE) kill 2>/dev/null || true; \
	    rm -f $(PF_PID_FILE); \
	fi
	@pkill -f "kubectl port-forward -n $(NAMESPACE)" 2>/dev/null || true

# ── Статус ───────────────────────────────────────────────────────────────────
status:
	@echo "==> Поды:"
	@kubectl get pods -n $(NAMESPACE)
	@echo ""
	@echo "==> Реплики:"
	@kubectl get deploy -n $(NAMESPACE) -o custom-columns=NAME:.metadata.name,READY:.status.readyReplicas,DESIRED:.spec.replicas
	@echo ""
	@echo "==> HPA:"
	@kubectl get hpa -n $(NAMESPACE)
	@echo ""
	@echo "==> БД на хосте:"
	@docker ps --filter "name=boiler-" --format "  {{.Names}}\t{{.Status}}\t{{.Ports}}"

logs:
	@if [ -z "$(SVC)" ]; then \
	    echo "укажи: make logs SVC=api  (или anomaly-service / notification-worker / nginx / kafka / opensearch / grafana / prometheus / jaeger / kafka-ui)"; \
	    exit 1; \
	fi
	kubectl logs -n $(NAMESPACE) -l app.kubernetes.io/component=$(SVC) --tail=100 -f 2>/dev/null || \
	kubectl logs -n $(NAMESPACE) -l app=$(SVC) --tail=100 -f

# ── Бэкапы и восстановление ──────────────────────────────────────────────────
backup-now:
	@echo "==> Postgres dump..."
	docker exec boiler-postgres-backup /backup.sh
	@echo "==> Influx backup..."
	@ts=$$(date +%Y-%m-%d-%H-%M); \
	    docker exec boiler-influx-backup influx backup --host http://influxdb:8086 --token dev-token /backups/$$ts && \
	    echo "  -> /backups/$$ts"

list-backups:
	@echo "==> Postgres (infra/databases/backups/postgres):"
	@ls -la infra/databases/backups/postgres/last/ 2>/dev/null || echo "  пусто"
	@echo ""
	@echo "==> InfluxDB (infra/databases/backups/influx):"
	@ls -1 infra/databases/backups/influx/ 2>/dev/null | tail -10 || echo "  пусто"

# Восстановить Postgres из дампа: make restore-postgres FILE=last/boiler_telemetry-latest.sql.gz
restore-postgres:
	@if [ -z "$(FILE)" ]; then \
	    echo "укажи: make restore-postgres FILE=last/boiler_telemetry-latest.sql.gz"; \
	    echo "(пути относительно infra/databases/backups/postgres/)"; exit 1; \
	fi
	@echo "==> ВНИМАНИЕ: текущая БД boiler_telemetry будет дропнута!"
	@echo "    Восстановление из: $(FILE)"
	@read -p "    Продолжить? [y/N] " ans && [ "$$ans" = "y" ]
	docker exec boiler-postgres dropdb -U postgres --if-exists boiler_telemetry
	docker exec boiler-postgres createdb -U postgres boiler_telemetry
	gunzip -c "infra/databases/backups/postgres/$(FILE)" | docker exec -i boiler-postgres psql -U postgres -d boiler_telemetry
	@echo "==> Перезапускаю приложения чтобы они переподключились..."
	kubectl -n $(NAMESPACE) rollout restart deploy/boiler-api deploy/boiler-notification-worker
	@echo "✓ Восстановлено."

reset-grafana:
	@p=$$(kubectl get pods -n $(NAMESPACE) -l app=grafana -o jsonpath='{.items[0].metadata.name}'); \
	    kubectl exec -n $(NAMESPACE) $$p -- grafana-cli admin reset-admin-password admin

# ── Снос ─────────────────────────────────────────────────────────────────────
down: ports-stop
	-helm uninstall $(HELM_RELEASE) -n $(NAMESPACE)
	-kubectl delete namespace $(NAMESPACE) --wait=false
	-docker compose -f $(DB_COMPOSE) down

clean: down
	-docker compose -f $(DB_COMPOSE) down -v
	-minikube delete
