ARG DOTNETSDK_VERSION
# debian 10 image
FROM mcr.microsoft.com/dotnet/sdk:$DOTNETSDK_VERSION-buster-slim
# ubuntu image
# FROM mcr.microsoft.com/dotnet/sdk:$DOTNETSDK_VERSION-focal


# Instructions to install latest Mono from
# https://www.mono-project.com/download/stable/#download-lin-debian
RUN apt-get update \
    && apt install -y apt-transport-https dirmngr gnupg ca-certificates \
    && apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys 3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF \
    && echo "deb https://download.mono-project.com/repo/debian stable-buster main" | tee /etc/apt/sources.list.d/mono-official-stable.list

# Instructions to install .NET Core runtimes from
# https://docs.microsoft.com/en-us/dotnet/core/install/linux-package-manager-debian10
RUN wget https://packages.microsoft.com/config/debian/10/packages-microsoft-prod.deb -O packages-microsoft-prod.deb \
    && dpkg -i packages-microsoft-prod.deb

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
        mono-complete \
        apt-transport-https \
        aspnetcore-runtime-2.1 \
        aspnetcore-runtime-3.0 \
        aspnetcore-runtime-3.1 \
    && gem install --no-document fpm

ENV CXX=clang++
ENV CC=clang

WORKDIR /project