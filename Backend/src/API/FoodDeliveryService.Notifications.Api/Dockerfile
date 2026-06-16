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
COPY ["Directory.Build.props", "."]
COPY ["src/API/FoodDeliveryService.Notifications.Api/FoodDeliveryService.Notifications.Api.csproj", "src/API/FoodDeliveryService.Notifications.Api/"]
COPY ["src/Modules/Notifications/FoodDeliveryService.Modules.Notifications.Infrastructure/FoodDeliveryService.Modules.Notifications.Infrastructure.csproj", "src/Modules/Notifications/FoodDeliveryService.Modules.Notifications.Infrastructure/"]
COPY ["src/Common/FoodDeliveryService.Common.Infrastructure/FoodDeliveryService.Common.Infrastructure.csproj", "src/Common/FoodDeliveryService.Common.Infrastructure/"]
COPY ["src/Common/FoodDeliveryService.Common.Application/FoodDeliveryService.Common.Application.csproj", "src/Common/FoodDeliveryService.Common.Application/"]
COPY ["src/Common/FoodDeliveryService.Common.Domain/FoodDeliveryService.Common.Domain.csproj", "src/Common/FoodDeliveryService.Common.Domain/"]
COPY ["src/Modules/Notifications/FoodDeliveryService.Modules.Notifications.Application/FoodDeliveryService.Modules.Notifications.Application.csproj", "src/Modules/Notifications/FoodDeliveryService.Modules.Notifications.Application/"]
COPY ["src/Modules/Notifications/FoodDeliveryService.Modules.Notifications.Domain/FoodDeliveryService.Modules.Notifications.Domain.csproj", "src/Modules/Notifications/FoodDeliveryService.Modules.Notifications.Domain/"]
COPY ["src/Modules/Notifications/FoodDeliveryService.Modules.Notifications.IntegrationEvents/FoodDeliveryService.Modules.Notifications.IntegrationEvents.csproj", "src/Modules/Notifications/FoodDeliveryService.Modules.Notifications.IntegrationEvents/"]
COPY ["src/Modules/Notifications/FoodDeliveryService.Modules.Notifications.Presentation/FoodDeliveryService.Modules.Notifications.Presentation.csproj", "src/Modules/Notifications/FoodDeliveryService.Modules.Notifications.Presentation/"]
COPY ["src/Common/FoodDeliveryService.Common.Presentation/FoodDeliveryService.Common.Presentation.csproj", "src/Common/FoodDeliveryService.Common.Presentation/"]
COPY ["src/Modules/Orders/FoodDeliveryService.Modules.Orders.IntegrationEvents/FoodDeliveryService.Modules.Orders.IntegrationEvents.csproj", "src/Modules/Orders/FoodDeliveryService.Modules.Orders.IntegrationEvents/"]
COPY ["src/Modules/Restaurants/FoodDeliveryService.Modules.Restaurants.IntegrationEvents/FoodDeliveryService.Modules.Restaurants.IntegrationEvents.csproj", "src/Modules/Restaurants/FoodDeliveryService.Modules.Restaurants.IntegrationEvents/"]
COPY ["src/Modules/Users/FoodDeliveryService.Modules.Users.IntegrationEvents/FoodDeliveryService.Modules.Users.IntegrationEvents.csproj", "src/Modules/Users/FoodDeliveryService.Modules.Users.IntegrationEvents/"]
RUN dotnet restore "./src/API/FoodDeliveryService.Notifications.Api/FoodDeliveryService.Notifications.Api.csproj"
COPY . .
WORKDIR "/src/src/API/FoodDeliveryService.Notifications.Api"
RUN dotnet build "./FoodDeliveryService.Notifications.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./FoodDeliveryService.Notifications.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FoodDeliveryService.Notifications.Api.dll"]