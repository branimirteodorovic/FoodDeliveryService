# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["src/API/FoodDeliveryService.Support.Api/FoodDeliveryService.Support.Api.csproj", "src/API/FoodDeliveryService.Support.Api/"]
COPY ["src/Modules/Support/FoodDeliveryService.Modules.Support.Infrastructure/FoodDeliveryService.Modules.Support.Infrastructure.csproj", "src/Modules/Support/FoodDeliveryService.Modules.Support.Infrastructure/"]
COPY ["src/Common/FoodDeliveryService.Common.Infrastructure/FoodDeliveryService.Common.Infrastructure.csproj", "src/Common/FoodDeliveryService.Common.Infrastructure/"]
COPY ["src/Common/FoodDeliveryService.Common.Application/FoodDeliveryService.Common.Application.csproj", "src/Common/FoodDeliveryService.Common.Application/"]
COPY ["src/Common/FoodDeliveryService.Common.Domain/FoodDeliveryService.Common.Domain.csproj", "src/Common/FoodDeliveryService.Common.Domain/"]
COPY ["src/Modules/Support/FoodDeliveryService.Modules.Support.Application/FoodDeliveryService.Modules.Support.Application.csproj", "src/Modules/Support/FoodDeliveryService.Modules.Support.Application/"]
COPY ["src/Modules/Support/FoodDeliveryService.Modules.Support.Domain/FoodDeliveryService.Modules.Support.Domain.csproj", "src/Modules/Support/FoodDeliveryService.Modules.Support.Domain/"]
COPY ["src/Modules/Support/FoodDeliveryService.Modules.Support.IntegrationEvents/FoodDeliveryService.Modules.Support.IntegrationEvents.csproj", "src/Modules/Support/FoodDeliveryService.Modules.Support.IntegrationEvents/"]
COPY ["src/Modules/Support/FoodDeliveryService.Modules.Support.Presentation/FoodDeliveryService.Modules.Support.Presentation.csproj", "src/Modules/Support/FoodDeliveryService.Modules.Support.Presentation/"]
COPY ["src/Common/FoodDeliveryService.Common.Presentation/FoodDeliveryService.Common.Presentation.csproj", "src/Common/FoodDeliveryService.Common.Presentation/"]
COPY ["src/Modules/Users/FoodDeliveryService.Modules.Users.IntegrationEvents/FoodDeliveryService.Modules.Users.IntegrationEvents.csproj", "src/Modules/Users/FoodDeliveryService.Modules.Users.IntegrationEvents/"]
RUN dotnet restore "./src/API/FoodDeliveryService.Support.Api/FoodDeliveryService.Support.Api.csproj"
COPY . .
WORKDIR "/src/src/API/FoodDeliveryService.Support.Api"
RUN dotnet build "./FoodDeliveryService.Support.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./FoodDeliveryService.Support.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FoodDeliveryService.Support.Api.dll"]
