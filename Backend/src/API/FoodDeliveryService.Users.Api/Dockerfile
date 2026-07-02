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
COPY ["src/API/FoodDeliveryService.Users.Api/FoodDeliveryService.Users.Api.csproj", "src/API/FoodDeliveryService.Users.Api/"]
COPY ["src/Modules/Users/FoodDeliveryService.Modules.Users.Infrastructure/FoodDeliveryService.Modules.Users.Infrastructure.csproj", "src/Modules/Users/FoodDeliveryService.Modules.Users.Infrastructure/"]
COPY ["src/Common/FoodDeliveryService.Common.Infrastructure/FoodDeliveryService.Common.Infrastructure.csproj", "src/Common/FoodDeliveryService.Common.Infrastructure/"]
COPY ["src/Common/FoodDeliveryService.Common.Application/FoodDeliveryService.Common.Application.csproj", "src/Common/FoodDeliveryService.Common.Application/"]
COPY ["src/Common/FoodDeliveryService.Common.Domain/FoodDeliveryService.Common.Domain.csproj", "src/Common/FoodDeliveryService.Common.Domain/"]
COPY ["src/Modules/Users/FoodDeliveryService.Modules.Users.Application/FoodDeliveryService.Modules.Users.Application.csproj", "src/Modules/Users/FoodDeliveryService.Modules.Users.Application/"]
COPY ["src/Modules/Users/FoodDeliveryService.Modules.Users.Domain/FoodDeliveryService.Modules.Users.Domain.csproj", "src/Modules/Users/FoodDeliveryService.Modules.Users.Domain/"]
COPY ["src/Modules/Users/FoodDeliveryService.Modules.Users.IntegrationEvents/FoodDeliveryService.Modules.Users.IntegrationEvents.csproj", "src/Modules/Users/FoodDeliveryService.Modules.Users.IntegrationEvents/"]
COPY ["src/Modules/Users/FoodDeliveryService.Modules.Users.Presentation/FoodDeliveryService.Modules.Users.Presentation.csproj", "src/Modules/Users/FoodDeliveryService.Modules.Users.Presentation/"]
COPY ["src/Common/FoodDeliveryService.Common.Presentation/FoodDeliveryService.Common.Presentation.csproj", "src/Common/FoodDeliveryService.Common.Presentation/"]
RUN dotnet restore "./src/API/FoodDeliveryService.Users.Api/FoodDeliveryService.Users.Api.csproj"
COPY . .
WORKDIR "/src/src/API/FoodDeliveryService.Users.Api"
RUN dotnet build "./FoodDeliveryService.Users.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./FoodDeliveryService.Users.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FoodDeliveryService.Users.Api.dll"]