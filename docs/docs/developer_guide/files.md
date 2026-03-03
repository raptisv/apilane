# Files

Apilane provides built-in file storage for your applications. Files are stored on the API server's file system and their metadata is tracked in the `Files` system entity.

## How It Works

- Files are uploaded via the **Files** controller (not the Data controller)
- Each file gets a metadata record in the `Files` entity with properties like `Name`, `Size`, `UID`, and `Public`
- File access is governed by the same [security rules](security.md) as any other entity
- The maximum allowed file size is configurable per application

## Uploading Files

Upload a file using a `multipart/form-data` POST request:

```
POST https://my.api.server/api/Files/Post
x-application-token: {appToken}
Content-Type: multipart/form-data

fileUpload: (binary file data)
```

**Query parameters:**

| Parameter | Required | Default | Description |
|---|---|---|---|
| `uid` | No | Auto-generated GUID | A custom unique identifier for the file. Accepts `a-z`, `A-Z`, `0-9`, `_`, `-`. If a file with the same UID exists, it will be overwritten. |
| `public` | No | `false` | If `true`, the file is accessible to all users regardless of authorization rules. |

**Response:** The new file's `ID` (integer).

!!!info "Use case for public files"
    Set `public=true` for assets like profile pictures or images used in HTML pages that need to be accessible without authentication.

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

The response is the raw file binary with a `Content-Type` of `application/octet-stream`. Client caching is set to 60 minutes.

!!!info "Tip"
    You can use the Download URL directly in HTML `<img>` tags or as download links.

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
| `pageSize` | `20` | Records per page (max 1000) |
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

File access is controlled by the same role-based security rules as entities. Navigate to the **Security** section of your application in the Portal to configure:

- Which roles can upload files (POST)
- Which roles can list/view file metadata (GET)
- Which roles can delete files (DELETE)

Files marked as `public=true` bypass read authorization and are accessible to anyone with the download URL.

## SDK Usage

``` csharp
// Upload a file
byte[] fileBytes = System.IO.File.ReadAllBytes("photo.jpg");
var uploadResponse = await _apilaneService.PostFileAsync(
    FilePostRequest.New()
        .WithAuthToken(authToken)
        .WithFileName("photo.jpg")
        .WithPublicFlag(false),
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
