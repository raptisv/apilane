---
description: "Where Apilane stores uploaded files: the local file system, Google Cloud Storage, AWS S3 or Azure Blob Storage."
---

# File Storage Providers

Apilane supports four file storage providers for uploaded files. Files can be stored on the local server file system or in cloud object storage.

The provider is a setting of the API server (`FileStorage`): it applies to every application on that server, and the files of an application are kept apart by its token (`{applicationToken}/files/{fileUID}`). Changing the provider does not move existing files (see [Migration](#migration)).

!!!warning "Delete and Rebuild"
    Deleting or rebuilding an application removes only the files in the API server's own `FilesPath` folder. Objects in a cloud bucket or container stay: delete the `{applicationToken}/` prefix yourself.

!!!info "What Apilane manages"
    Apilane handles file upload, download, deletion, and metadata tracking through the `Files` system entity. The choice of storage provider determines where file binaries are physically stored.

## Local File System

**Best for:** Getting started, prototyping, single-server deployments.

No additional configuration is required beyond the `FilesPath` setting, which every API server needs. Apilane stores uploaded files directly on the API server's file system, in `{FilesPath}/{applicationToken}/files/`.

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
    You must mount a persistent volume that contains the `FilesPath` directory to prevent file loss when containers restart. The compose file in [Deployment](../deployment.md) sets `FilesPath=/etc/apilanewebapi/Files` and mounts the volume at `/etc/apilanewebapi`; see it for volume mapping guidance.

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

!!!info "Service account file required"
    The provider reads the service account JSON file named in `ConnectionString`: Workload Identity and application default credentials are not supported. Mount the key as a secret, as above.

---

## AWS S3

**Best for:** Applications deployed on AWS, S3-compatible storage (Backblaze B2, MinIO).

Stores files in an Amazon S3 bucket or S3-compatible storage.

**Configuration:**

```json
"FileStorage": {
  "Provider": "AwsS3",
  "ConnectionString": "accessKeyId=AKIA...;secretAccessKey=...;region=us-east-1",
  "BucketName": "my-app-files-bucket"
}
```

| Parameter | Description |
|---|---|
| `ConnectionString` | Format: `accessKeyId={key};secretAccessKey={secret};region={region}`, or `accessKeyId={key};secretAccessKey={secret};endpoint={host}` for S3-compatible storage. The names are not case-sensitive and both keys are required |
| `BucketName` | S3 bucket name (must already exist) |

### Setup Steps

1. **Create an S3 bucket** in your AWS account
2. **Create an IAM user** with `s3:PutObject`, `s3:GetObject`, `s3:DeleteObject` permissions on the bucket
3. **Generate access keys** for that user
4. **Set connection string** via environment variables or appsettings

```bash
# Docker environment variable example (the other settings every API server needs, Url, PortalUrl, FilesPath and InstallationKey, are left out)
docker run -e "FileStorage__ConnectionString=accessKeyId=AKIA...;secretAccessKey=...;region=us-east-1" \
           -e "FileStorage__BucketName=my-app-files-bucket" \
           -e "FileStorage__Provider=AwsS3" \
           raptis/apilane:api-{version}
```

!!!info "Access keys required"
    Both keys are required: IAM roles of EC2/EKS are not supported. Create an IAM user limited to the bucket and keep its keys in a secret.

### S3-Compatible Storage (Backblaze B2, MinIO)

For S3-compatible providers, use the `endpoint` parameter with the host name only (no `https://`). The connection always uses HTTPS and path-style addresses, so a MinIO server needs TLS:

```json
"ConnectionString": "accessKeyId=...;secretAccessKey=...;endpoint=s3.us-west-004.backblazeb2.com"
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
# Docker environment variable example (the other settings every API server needs, Url, PortalUrl, FilesPath and InstallationKey, are left out)
docker run -e "FileStorage__ConnectionString=DefaultEndpointsProtocol=https;AccountName=...;AccountKey=..." \
           -e "FileStorage__BucketName=my-container-name" \
           -e "FileStorage__Provider=AzureBlobStorage" \
           raptis/apilane:api-{version}
```

!!!info "Connection string required"
    The storage account connection string is required: Managed Identity is not supported. Keep the connection string in a secret.

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
- **Static credentials only**: the providers take an access key pair, a service account JSON file or a connection string. Managed identities (IAM roles, Workload Identity, Azure Managed Identity) are not supported, so scope each credential to the one bucket or container.
- **Bucket/container permissions**: Restrict access to only the API service account. Do not make buckets public unless required.
- **Rotate credentials regularly** and use least-privilege IAM policies.

See [Security Considerations](../security_considerations.md) for more details on credential management.

---

## Migration

To migrate from `LocalFileSystem` to a cloud provider:

1. **Upload existing files**: copy the content of `{FilesPath}/{applicationToken}/files/` of each application to the key prefix `{applicationToken}/files/` in your bucket or container, using the provider's CLI or SDK. Leave out the SQLite database file (`{applicationToken}.db`) that sits next to that folder
2. **Preserve the file structure**: `{applicationToken}/files/{fileUID}`
3. **Update `FileStorage` configuration** to point to the cloud provider
4. **Restart the API service**
5. **Verify file downloads** still work

!!!warning "Migration is manual"
    Apilane does not provide an automated migration tool. You must manually transfer files to maintain the same file structure (`{appToken}/files/{fileUID}`).
