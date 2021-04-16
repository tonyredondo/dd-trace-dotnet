#!/usr/bin/env bash
set -euox pipefail

# in case we are being run from outside this directory
cd "$(dirname "$0")"

ROOT_DIR="$(pwd)"
BUILD_DIR="$ROOT_DIR/build/_build"
IMAGE_NAME="dd-trace-dotnet/debian-base"

docker build \
   --build-arg DOTNETSDK_VERSION=5.0.201 \
   --tag dd-trace-dotnet/debian-base \
   --file "$BUILD_DIR/docker/linux.dockerfile" \
   "$BUILD_DIR"

docker run -it --rm \
    --mount type=bind,source="$ROOT_DIR",target=/project \
    $IMAGE_NAME \
    dotnet /build/bin/Debug/_build.dll "$@"