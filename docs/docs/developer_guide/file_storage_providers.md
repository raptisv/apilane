# File Storage Providers

Apilane supports four file storage providers for uploaded files. Files can be stored on the local server file system or in cloud object storage.

!!!info "What Apilane manages"
    Apilane handles file upload, download, deletion, and metadata tracking through the `Files` system entity. The choice of storage provider determines where file binaries are physically stored.

## Local File System

**Best for:** Getting started, prototyping, single-server deployments.

No additional configuration required beyond the `FilesPath` environment variable. Apilane stores uploaded files directly on the API server's file system.

**Configuration:**

```json
"FileStorage": {
  "Provider": "LocalFileSystem"
}
```

| Pros | Cons |
|---|---|
| Zero cloud account setup | Requires persistent volumes in Docker/Kubernetes |
| No additional cost | Not suitable for multi-region deployments |
| Simple deployment | Limited scalability |

!!!warning "Docker/Kubernetes deployments"
    You must mount a persistent volume to the `FilesPath` directory (`/etc/apilanewebapi` by default) to prevent file loss when containers restart. See [Deployment](../deployment.md) for volume mapping guidance.

---

## Google Cloud Storage (GCS)

**Best for:** Applications deployed on Google Cloud Platform, scalable cloud storage.

Stores files in a Google Cloud Storage bucket. Requires a service account with Storage Object Admin permissions.

**Configuration:**

```json
"FileStorage": {
  "Provider": "GoogleCloudStorage",
  "ConnectionString": "/run/secrets/gcs-sa.json",
  "BucketName": "my-app-files-bucket"
}
```

| Parameter | Description |
|---|---|
| `ConnectionString` | Path to service account JSON file (absolute path inside the container) |
| `BucketName` | GCS bucket name (must already exist) |

### Setup Steps

1. **Create a GCS bucket** in your Google Cloud project
2. **Create a service account** with `Storage Object Admin` role on the bucket
3. **Download the service account JSON key**
4. **Mount the JSON file as a secret** in Docker/Kubernetes:

```yaml
# Kubernetes Secret example
apiVersion: v1
kind: Secret
metadata:
  name: gcs-credentials
type: Opaque
stringData:
  gcs-sa.json: |
    {
      "type": "service_account",
      "project_id": "my-project",
      "private_key_id": "...",
      ...
    }
---
# Mount in Deployment
volumes:
  - name: gcs-secret
    secret:
      secretName: gcs-credentials
volumeMounts:
  - name: gcs-secret
    mountPath: /run/secrets
    readOnly: true
```

!!!tip "Workload Identity"
    For GKE deployments, use [Workload Identity](https://cloud.google.com/kubernetes-engine/docs/how-to/workload-identity) to avoid managing service account JSON files.

---

## AWS S3

**Best for:** Applications deployed on AWS, S3-compatible storage (Backblaze B2, MinIO).

Stores files in an Amazon S3 bucket or S3-compatible storage.

**Configuration:**

```json
"FileStorage": {
  "Provider": "AwsS3",
  "ConnectionString": "AccessKey=AKIA...;SecretKey=...;Region=us-east-1",
  "BucketName": "my-app-files-bucket"
}
```

| Parameter | Description |
|---|---|
| `ConnectionString` | Format: `AccessKey={key};SecretKey={secret};Region={region}` or `AccessKey={key};SecretKey={secret};ServiceUrl={url}` for S3-compatible endpoints |
| `BucketName` | S3 bucket name (must already exist) |

### Setup Steps

1. **Create an S3 bucket** in your AWS account
2. **Create an IAM user or role** with `s3:PutObject`, `s3:GetObject`, `s3:DeleteObject` permissions on the bucket
3. **Generate access keys** (or use IAM roles for EC2/EKS)
4. **Set connection string** via environment variables or appsettings

```bash
# Docker environment variable example
docker run -e "FileStorage__ConnectionString=AccessKey=AKIA...;SecretKey=...;Region=us-east-1" \
           -e "FileStorage__BucketName=my-app-files-bucket" \
           -e "FileStorage__Provider=AwsS3" \
           raptis/apilane:api-8.4.9
```

!!!tip "IAM Roles"
    For EC2/EKS deployments, use IAM roles instead of access keys. Omit `AccessKey` and `SecretKey` from the connection string and assign the role to the instance/pod.

### S3-Compatible Storage (Backblaze B2, MinIO)

For S3-compatible providers, use the `ServiceUrl` parameter:

```json
"ConnectionString": "AccessKey=...;SecretKey=...;ServiceUrl=https://s3.us-west-004.backblazeb2.com"
```

---

## Azure Blob Storage

**Best for:** Applications deployed on Microsoft Azure.

Stores files in an Azure Blob Storage container.

**Configuration:**

```json
"FileStorage": {
  "Provider": "AzureBlobStorage",
  "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=...;EndpointSuffix=core.windows.net",
  "BucketName": "my-container-name"
}
```

| Parameter | Description |
|---|---|
| `ConnectionString` | Azure Storage Account connection string |
| `BucketName` | Azure Blob container name (must already exist) |

### Setup Steps

1. **Create an Azure Storage Account**
2. **Create a Blob container** in the storage account
3. **Copy the connection string** from the Azure Portal (Access Keys section)
4. **Set connection string** via environment variables or appsettings

```bash
# Docker environment variable example
docker run -e "FileStorage__ConnectionString=DefaultEndpointsProtocol=https;AccountName=...;AccountKey=..." \
           -e "FileStorage__BucketName=my-container-name" \
           -e "FileStorage__Provider=AzureBlobStorage" \
           raptis/apilane:api-8.4.9
```

!!!tip "Managed Identity"
    For AKS deployments, use [Managed Identity](https://learn.microsoft.com/en-us/azure/aks/use-managed-identity) instead of connection strings for better security.

---

## Choosing a Provider

| Criteria | LocalFileSystem | GoogleCloudStorage | AwsS3 | AzureBlobStorage |
|---|---|---|---|---|
| Setup complexity | None | Moderate | Moderate | Moderate |
| Cost | Free (uses server disk) | Pay per GB stored + requests | Pay per GB stored + requests | Pay per GB stored + requests |
| Scalability | Limited | High | High | High |
| Multi-region | No | Yes | Yes | Yes |
| Best for | Dev / Small apps / Single server | Google Cloud deployments | AWS deployments / S3-compatible storage | Azure deployments |

---

## Security Considerations

- **Never commit credentials** to source control. Use environment variables, Docker secrets, or Kubernetes secrets.
- **Use managed identities** when available (Workload Identity for GKE, IAM Roles for EKS, Managed Identity for AKS).
- **Bucket/container permissions**: Restrict access to only the API service account. Do not make buckets public unless required.
- **Rotate credentials regularly** and use least-privilege IAM policies.

See [Security Considerations](../security_considerations.md) for more details on credential management.

---

## Migration

To migrate from `LocalFileSystem` to a cloud provider:

1. **Upload existing files** from `FilesPath` to your cloud bucket using the provider's CLI or SDK
2. **Preserve the file structure**: `{applicationToken}/files/{fileUID}`
3. **Update `FileStorage` configuration** to point to the cloud provider
4. **Restart the API service**
5. **Verify file downloads** still work

!!!warning "Migration is manual"
    Apilane does not provide an automated migration tool. You must manually transfer files to maintain the same file structure (`{appToken}/files/{fileUID}`).
