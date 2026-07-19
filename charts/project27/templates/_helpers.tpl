{{/*
Chart name, truncated/DNS-safe.
*/}}
{{- define "project27.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{/*
Release-qualified fullname, used as the base for all resource names.
*/}}
{{- define "project27.fullname" -}}
{{- if .Values.fullnameOverride -}}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- $name := default .Chart.Name .Values.nameOverride -}}
{{- if contains $name .Release.Name -}}
{{- .Release.Name | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}
{{- end -}}

{{- define "project27.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{/*
Common labels, shared by every resource in the chart.
*/}}
{{- define "project27.labels" -}}
helm.sh/chart: {{ include "project27.chart" . }}
app.kubernetes.io/name: {{ include "project27.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- if .Chart.AppVersion }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- end }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end -}}

{{/*
Per-component selector labels. These MUST be the only labels used in any
Deployment's spec.selector / pod template labels and its Service's
spec.selector — sharing one selector-labels helper across server and web
would make each Service also match the other component's pods.
*/}}
{{- define "project27.selectorLabels.server" -}}
app.kubernetes.io/name: {{ include "project27.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/component: server
{{- end -}}

{{- define "project27.selectorLabels.web" -}}
app.kubernetes.io/name: {{ include "project27.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/component: web
{{- end -}}

{{- define "project27.selectorLabels.mcp" -}}
app.kubernetes.io/name: {{ include "project27.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/component: mcp
{{- end -}}

{{- define "project27.serviceAccountName" -}}
{{- if .Values.serviceAccount.create -}}
{{- default (include "project27.fullname" .) .Values.serviceAccount.name -}}
{{- else -}}
{{- default "default" .Values.serviceAccount.name -}}
{{- end -}}
{{- end -}}

{{/*
Resolved image tag, shared by both components.
*/}}
{{- define "project27.imageTag" -}}
{{- .Values.image.tag | default .Chart.AppVersion -}}
{{- end -}}
