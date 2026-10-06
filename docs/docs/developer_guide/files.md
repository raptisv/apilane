---
description: "Upload, download and manage files in Apilane, with metadata tracked in the Files entity and the binaries on disk or in cloud storage."
---

# Files

Apilane provides built-in file storage for your applications. Files can be stored on the local server file system or in cloud object storage (Google Cloud Storage, AWS S3, Azure Blob Storage). File metadata is tracked in the `Files` system entity.

!!!info "Storage Provider Configuration"
    The file storage location is configured via the `FileStorage` section in `appsettings.json`. See [File Storage Providers](file_storage_providers.md) for setup instructions for each provider.

## How It Works

- Files are uploaded via the **Files** controller (not the Data controller)
- Each file gets a metadata record in the `Files` entity with `Name`, `UID` (a GUID), `Size` (in MB), `Owner` and `Created`. A file cannot be edited: upload a new one and delete the old one. The `Files` entity cannot be used through the Data endpoints
- File access is governed by the same [security rules](security.md) as any other entity
- The maximum allowed file size is set per application (**Maximum file size (KB)** on the **Security** tab): 100 KB for a new application, up to 25,600 KB (25 MB). A larger upload is refused with `Maximum file size {n} KB`
- Physical files are stored according to the configured storage provider (local file system or cloud storage)

## Uploading Files

Upload a file using a `multipart/form-data` POST request:

```
POST https://my.api.server/api/Files/Post
x-application-token: {appToken}
Content-Type: multipart/form-data

fileUpload: (binary file data)
```

**Response:** The new file's `ID` (integer).

Files whose extension is in the API server's `InvalidFilesExtentions` setting (spelled that way) are refused. The default list is `.exe`, `.vbs`, `.msi`, `.jar`, `.bat`, `.cmd`, `.vbe`, `.js`, `.jsp` and `.lnk`. A refused upload answers with the general error `Something went wrong`; the reason shows only to the people who manage the application, in the Portal. File uploads cannot be signed: authenticate them with the `Authorization: Bearer {authToken}` header (a request with an `x-auth-signature` header is refused).

## Downloading Files

Download a file by its **ID** or **UID**:

```
GET https://my.api.server/api/Files/Download?fileID={id}
x-application-token: {appToken}
```

```
GET https://my.api.server/api/Files/Download?fileUID={uid}
x-application-token: {appToken}
```

Downloading needs the same `get` access to `Files` as listing. The response is the raw file binary with a `Content-Type` header based on the file's name, sent as an attachment named after the file. It carries `Cache-Control: max-age=31536000` (one year), so browsers keep a downloaded file for that long.

A browser navigation (`<img src>`, `<a href>`) cannot send headers, so give the application token as the query parameter `appToken` and, when anonymous callers may not read the file, the user's token as `authToken`:

```
GET https://my.api.server/api/Files/Download?fileUID={uid}&appToken={appToken}&authToken={authToken}
```

The token is then part of the URL (browser history, server logs), so use it only where you must.

## Listing Files

Retrieve file metadata records (not the actual file content):

```
GET https://my.api.server/api/Files/Get
x-application-token: {appToken}
```

This endpoint supports the same [filtering, sorting, and paging](filtering_sorting.md) parameters as the Data endpoints.

| Parameter | Default | Description |
|---|---|---|
| `pageIndex` | `1` | Page number |
| `pageSize` | `20` | Records per page, 1 to 1000 (a value outside it becomes 1000) |
| `filter` | `null` | JSON filter expression |
| `sort` | `null` | JSON sort expression |
| `properties` | all | Comma-separated list of properties to return |
| `getTotal` | `false` | Include total record count in response |

## Getting a Single File Record

Retrieve metadata for a specific file by its ID:

```
GET https://my.api.server/api/Files/GetByID?id={id}
x-application-token: {appToken}
```

## Deleting Files

Delete one or more files by their IDs:

```
DELETE https://my.api.server/api/Files/Delete?ids=1,2,3
x-application-token: {appToken}
```

**Response:** An array of the deleted file IDs.

## File Security

File access is controlled by the same role-based security rules as entities. Open the **Security** tab of your application in the Portal to configure:

- Which roles can upload files (POST)
- Which roles can list/view file metadata and download files (GET)
- Which roles can delete files (DELETE)

## SDK Usage

``` csharp
// Upload a file
byte[] fileBytes = System.IO.File.ReadAllBytes("photo.jpg");
var uploadResponse = await _apilaneService.PostFileAsync(
    FilePostRequest.New()
        .WithAuthToken(authToken)
        .WithFileName("photo.jpg"),
    fileBytes);

// List files
var filesResponse = await _apilaneService.GetFilesAsync<MyFileEntity>(
    FileGetListRequest.New()
        .WithAuthToken(authToken));

// Delete files
var deleteResponse = await _apilaneService.DeleteFileAsync(
    FileDeleteRequest.New()
        .WithAuthToken(authToken)
        .AddIdToDelete(fileId));
```
