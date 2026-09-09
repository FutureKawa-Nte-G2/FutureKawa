{{- define "futurekawa-siege.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "futurekawa-siege.fullname" -}}
{{- if .Values.fullnameOverride -}}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name (default .Chart.Name .Values.nameOverride) | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}

{{- define "futurekawa-siege.labels" -}}
app.kubernetes.io/name: {{ include "futurekawa-siege.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version }}
{{- end -}}

{{/*
Environnement commun aux trois usages de l'image du siège : l'API, le Job de
migration et le Job de seed. Un seul endroit à corriger, donc aucun risque que
le seed tourne contre une base différente de celle que l'API lira.
*/}}
{{- define "futurekawa-siege.apiEnv" -}}
- name: PGUSER
  valueFrom:
    secretKeyRef:
      name: {{ include "futurekawa-siege.fullname" . }}-db-app
      key: username
- name: PGPASSWORD
  valueFrom:
    secretKeyRef:
      name: {{ include "futurekawa-siege.fullname" . }}-db-app
      key: password
- name: ConnectionStrings__Default
  value: Host={{ include "futurekawa-siege.fullname" . }}-db-rw;Port=5432;Database={{ .Values.postgres.db }};Username=$(PGUSER);Password=$(PGPASSWORD)
# Requis même par --migrate : Program.cs lève dessus avant builder.Build(),
# donc avant que le flag ne soit lu.
- name: Jwt__Secret
  valueFrom:
    secretKeyRef:
      name: {{ include "futurekawa-siege.fullname" . }}-secrets
      key: JWT_SECRET
{{- if .Values.odoo.enabled }}
- name: Odoo__Url
  value: http://{{ include "futurekawa-siege.fullname" . }}-odoo:8069
- name: Odoo__Db
  value: {{ .Values.odoo.dbName | quote }}
- name: Odoo__Username
  value: admin@admin.com
- name: Odoo__Password
  valueFrom:
    secretKeyRef:
      name: {{ include "futurekawa-siege.fullname" . }}-secrets
      key: ODOO_ADMIN_PASSWORD
- name: Odoo__WebhookToken
  valueFrom:
    secretKeyRef:
      name: {{ include "futurekawa-siege.fullname" . }}-secrets
      key: ODOO_WEBHOOK_TOKEN
{{- end }}
{{- end -}}

{{/*
Attente de la base. CNPG met un moment à promouvoir le primaire ; sans cette
attente les Jobs échoueraient sur leurs premiers essais et consommeraient leur
backoffLimit avant que la base ne réponde.
*/}}
{{- define "futurekawa-siege.waitForDb" -}}
- name: wait-for-db
  image: busybox:1.36
  command:
    - sh
    - -c
    - |
      until nc -z {{ include "futurekawa-siege.fullname" . }}-db-rw 5432; do
        echo "base du siège pas encore prête"; sleep 3;
      done
{{- end -}}
