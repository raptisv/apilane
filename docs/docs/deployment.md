# Deployment


An `Apilane Instance` consists of 2 images thus it can be deployed in any environment.

!!!info "Environment variables"
    Please visit [environment variables](/getting_started/#environment-variables) section for a description of the available environment variables.

## Docker

Execute the provided [docker-compose.yaml](assets/docker-compose.yaml) using the command `docker-compose -p apilane up -d` which will setup the Portal and the Api services on docker.
You may then access the portal on [http://localhost:5000](http://localhost:5000).

## k8s

Apilane can be deployed to k8s, on premise or in any cloud provider. There are many deployment configurations required so it is impossible to provide a commonly acceptable yaml sample.

!!!warning "Important"
    Note that for k8s deployments, all instances of the `Apilane Portal` and `Apilane API` services must be able to access the application files. Since on-disk files in a container are ephemeral a [persistent volume](https://kubernetes.io/docs/concepts/storage/persistent-volumes/) is required. You will have to map the following paths from environment variables
    
    - For Portal map `FilesPath`
    - For API map `FilesPath`

    Both can point to the same path since the files are not conflicting.