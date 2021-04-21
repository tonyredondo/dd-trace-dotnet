﻿ARG DOTNETSDK_VERSION
# debian 10 image
FROM mcr.microsoft.com/dotnet/sdk:$DOTNETSDK_VERSION-buster-slim as builder
# ubuntu image
# FROM mcr.microsoft.com/dotnet/sdk:$DOTNETSDK_VERSION-focal

RUN apt-get update \
    && apt-get -y upgrade \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --fix-missing \
        git \
        wget \
        curl \
        cmake \
        make \
        llvm \
        clang \
        gcc \
        build-essential \
        rpm \
        ruby \
        ruby-dev \
        rubygems \
    && gem install --no-document fpm


ENV CXX=clang++
ENV CC=clang

# Copy the build files in
COPY . /build
RUN dotnet build /build

WORKDIR /project

FROM builder as tester

# Install .NET Core runtimes using install script
RUN curl -sSL https://dot.net/v1/dotnet-install.sh --output dotnet-install.sh \
    && chmod +x ./dotnet-install.sh \
    && ./dotnet-install.sh --runtime aspnetcore --channel 2.1 --install-dir /usr/share/dotnet --no-path \
    && ./dotnet-install.sh --runtime aspnetcore --channel 3.0 --install-dir /usr/share/dotnet --no-path \
    && ./dotnet-install.sh --runtime aspnetcore --channel 3.1 --install-dir /usr/share/dotnet --no-path \
    && rm dotnet-install.sh
