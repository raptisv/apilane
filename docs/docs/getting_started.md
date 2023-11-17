# Getting started

An `Apilane Instance` consists of 2 images thus it can be deployed in any environment. Since docker is widely known and commonly used, we provide a ready to execute docker compose file.

## Docker compose

Execute the provided [docker-compose.yaml](assets/docker-compose.yaml) using the command `docker-compose -p apilane up -d` which will setup the Portal and the Api services on docker.
You may then access the portal on [http://localhost:5000](http://localhost:5000).

## Portal access

Use the following default credentials to login to the portal:

- Email: **admin@admin.com**
- Password: **admin**

!!!warning "Important"
    You may alter the admin email **only** by overriding the relevant environment variable as described below. You may change the password after the first login to the portal.

## Environment variables

Regardless the deployment of choice (docker, k8s or cloud) you can override the default environment variables according to your setup of preference.

### Portal

- **Url** - Default value `http://0.0.0.0:5000`
	- The url where the Portal is served.
- **ApiUrl** - Default value `http://127.0.0.1:5001`
	- The url to the initial API service. Change it according to your deployment setup.
- **FilesPath** - Default value `/etc/apilanewebportal` 
	- The path to the Portal's files. This is where the portal storage provider lives. The portal storage provider of preference is Sqlite. The only configurable option is the path that the Sqlite database will be created. Leave the default value if there is no specific deployment requirement.
- **InstallationKey** - Default value `8dc64403-0f5b-4723-9aa7-42004841d838`
	- Can be any guid key, but should be the same with apilanewebapi. Portal and API communicate with each other and authorize access using this installation key. For that reason you are advised to keep it secret.
- **AdminEmail** - Default value `admin@admin.com`
	- The email for instance portal administrator. The admin user is created only once during the first instance deployment. You are advised to change it with your email before the deployment. Default password is `admin` which can be changed later from the portal.
	
### API

- **Url** - Default value `http://0.0.0.0:5001`
	- The url where the API is served.
- **PortalUrl** - Default value `http://127.0.0.1:5000`
	- The url to the Portal. Change it according to your deployment setup.
- **FilesPath** - Default value `/etc/apilanewebapi/Files`
	- The path to the API server files. All files generated from the server can be found under this path. The only configurable option is the path that those files will be created. Leave the default value if there is no specific deployment requirement.
- **InstallationKey** - Default value `8dc64403-0f5b-4723-9aa7-42004841d838`
	- Can be any guid key, but should be the same with apilanewebportal. Portal and API communicate with each other and authorize access using this installation key. For that reason you are advised to keep it secret.