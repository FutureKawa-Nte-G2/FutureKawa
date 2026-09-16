{{- define "futurekawa-data.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "futurekawa-data.fullname" -}}
{{- if .Values.fullnameOverride -}}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- $name := default .Chart.Name .Values.nameOverride -}}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}

{{- define "futurekawa-data.labels" -}}
app.kubernetes.io/name: {{ include "futurekawa-data.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version }}
futurekawa.io/pays: {{ .Values.pays | quote }}
{{- end -}}

{{- define "futurekawa-data.secretName" -}}
{{- printf "%s-secrets" (include "futurekawa-data.fullname" .) -}}
{{- end -}}
