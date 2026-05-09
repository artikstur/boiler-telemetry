$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot

helm uninstall boiler -n boiler
kubectl delete namespace boiler --ignore-not-found

docker compose -f "$root\infra\databases\docker-compose.yml" down
