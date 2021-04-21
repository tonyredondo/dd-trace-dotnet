ARG DOTNETSDK_VERSION
# debian 10 image
FROM mcr.microsoft.com/dotnet/sdk:$DOTNETSDK_VERSION-alpine3.12 as builder
# ubuntu image
# FROM mcr.microsoft.com/dotnet/sdk:$DOTNETSDK_VERSION-focal

RUN apk update \
    && apk upgrade \
    && apk add --no-cache --update \
        clang \
        cmake \
        git \
        bash \
        make \
        alpine-sdk \
        ruby \
        ruby-dev \
        ruby-etc \
    && gem install --no-document fpm

ENV IsAlpine=true

# Copy the build files in
COPY . /build
RUN dotnet build /build

WORKDIR /project