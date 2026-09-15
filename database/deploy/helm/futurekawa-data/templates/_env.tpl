{{/*
DATABASE_URL assemblée à partir du secret que CNPG génère pour l'owner
(<cluster>-app). Les identifiants ne transitent donc jamais par values.yaml.
Kubernetes interpole $(VAR) depuis les variables déclarées avant : c'est ce qui
permet de composer une URL à partir de deux secretKeyRef.
*/}}
{{- define "futurekawa-data.dbEnv" -}}
- name: PGUSER
  valueFrom:
    secretKeyRef:
      name: {{ include "futurekawa-data.fullname" . }}-postgres-app
      key: username
- name: PGPASSWORD
  valueFrom:
    secretKeyRef:
      name: {{ include "futurekawa-data.fullname" . }}-postgres-app
      key: password
- name: DATABASE_URL
  value: postgresql+asyncpg://$(PGUSER):$(PGPASSWORD)@{{ include "futurekawa-data.fullname" . }}-postgres-rw:5432/{{ .Values.postgres.db }}
{{- end -}}
