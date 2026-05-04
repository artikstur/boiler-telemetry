{{/*
Expand the name of the chart.
*/}}
{{- define "boiler-telemetry.name" -}}
{{- .Chart.Name | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Create a default fully qualified app name.
*/}}
{{- define "boiler-telemetry.fullname" -}}
{{- printf "%s" .Release.Name | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Common labels
*/}}
{{- define "boiler-telemetry.labels" -}}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{/*
Selector labels for a component
Usage: include "boiler-telemetry.selectorLabels" (dict "component" "api" "root" .)
*/}}
{{- define "boiler-telemetry.selectorLabels" -}}
app.kubernetes.io/name: {{ include "boiler-telemetry.name" .root }}
app.kubernetes.io/component: {{ .component }}
{{- end }}
