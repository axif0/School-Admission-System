# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["SchoolAdmission.sln", "./"]
COPY ["src/SchoolAdmission.Api/SchoolAdmission.Api.csproj", "src/SchoolAdmission.Api/"]
COPY ["tests/SchoolAdmission.Tests/SchoolAdmission.Tests.csproj", "tests/SchoolAdmission.Tests/"]

# Copy all source code first
COPY . .

# Restore and build together
RUN dotnet restore
RUN dotnet build -c Release

# Test stage for running tests inside Docker
FROM build AS test
WORKDIR /src/tests/SchoolAdmission.Tests
ENTRYPOINT ["dotnet", "test", "--no-build", "-c", "Release", "--logger:console;verbosity=normal"]

# Publish stage
FROM build AS publish
WORKDIR /src/src/SchoolAdmission.Api
RUN dotnet publish -c Release -o /app/publish --no-build

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SchoolAdmission.Api.dll"]
