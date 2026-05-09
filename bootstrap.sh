#!/usr/bin/env bash
# Установка зависимостей на свежую Linux VM (Ubuntu/Debian).
# Запускать один раз. После этого: make up.

set -euo pipefail

echo "==> Обновление apt..."
sudo apt-get update -y

# ── Docker ────────────────────────────────────────────────────────────────────
if ! command -v docker >/dev/null; then
    echo "==> Ставлю Docker..."
    curl -fsSL https://get.docker.com | sudo sh
    sudo usermod -aG docker "$USER"
    echo "  ⚠ Перелогинься (или выполни 'newgrp docker'), чтобы группа docker применилась без sudo."
fi

# ── kubectl ───────────────────────────────────────────────────────────────────
if ! command -v kubectl >/dev/null; then
    echo "==> Ставлю kubectl..."
    KVER=$(curl -L -s https://dl.k8s.io/release/stable.txt)
    curl -L -o /tmp/kubectl "https://dl.k8s.io/release/${KVER}/bin/linux/amd64/kubectl"
    sudo install -m 0755 /tmp/kubectl /usr/local/bin/kubectl
    rm /tmp/kubectl
fi

# ── minikube ──────────────────────────────────────────────────────────────────
if ! command -v minikube >/dev/null; then
    echo "==> Ставлю minikube..."
    curl -L -o /tmp/minikube https://storage.googleapis.com/minikube/releases/latest/minikube-linux-amd64
    sudo install -m 0755 /tmp/minikube /usr/local/bin/minikube
    rm /tmp/minikube
fi

# ── helm ──────────────────────────────────────────────────────────────────────
if ! command -v helm >/dev/null; then
    echo "==> Ставлю helm..."
    curl -fsSL https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash
fi

# ── make (на всякий) ──────────────────────────────────────────────────────────
if ! command -v make >/dev/null; then
    sudo apt-get install -y make
fi

echo ""
echo "✓ Все зависимости поставлены:"
docker --version
kubectl version --client --short 2>/dev/null || kubectl version --client
minikube version --short
helm version --short
make --version | head -1
echo ""
echo "Дальше:"
echo "  make up        — полный деплой"
echo "  make status    — посмотреть состояние"
