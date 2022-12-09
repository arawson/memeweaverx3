# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:6.0 as build-env
WORKDIR /memeweaver
COPY memeweaver/*.csproj .
RUN dotnet restore --use-current-runtime
COPY memeweaver .
# -o matches WORKDIR below
RUN dotnet publish --use-current-runtime --no-restore --os linux -c Release -o /publish

FROM mcr.microsoft.com/dotnet/runtime:6.0 as runtime
WORKDIR /publish
COPY --from=build-env /publish .
ENTRYPOINT "bash"
# ENTRYPOINT ["dotnet", "memeweaver.dll"]
