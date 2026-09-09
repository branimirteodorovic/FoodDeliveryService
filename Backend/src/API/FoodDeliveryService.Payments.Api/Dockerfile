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
COPY ["src/API/FoodDeliveryService.Payments.Api/FoodDeliveryService.Payments.Api.csproj", "src/API/FoodDeliveryService.Payments.Api/"]
COPY ["src/Modules/Payments/FoodDeliveryService.Modules.Payments.Infrastructure/FoodDeliveryService.Modules.Payments.Infrastructure.csproj", "src/Modules/Payments/FoodDeliveryService.Modules.Payments.Infrastructure/"]
COPY ["src/Common/FoodDeliveryService.Common.Infrastructure/FoodDeliveryService.Common.Infrastructure.csproj", "src/Common/FoodDeliveryService.Common.Infrastructure/"]
COPY ["src/Common/FoodDeliveryService.Common.Application/FoodDeliveryService.Common.Application.csproj", "src/Common/FoodDeliveryService.Common.Application/"]
COPY ["src/Common/FoodDeliveryService.Common.Domain/FoodDeliveryService.Common.Domain.csproj", "src/Common/FoodDeliveryService.Common.Domain/"]
COPY ["src/Modules/Payments/FoodDeliveryService.Modules.Payments.Application/FoodDeliveryService.Modules.Payments.Application.csproj", "src/Modules/Payments/FoodDeliveryService.Modules.Payments.Application/"]
COPY ["src/Modules/Payments/FoodDeliveryService.Modules.Payments.Domain/FoodDeliveryService.Modules.Payments.Domain.csproj", "src/Modules/Payments/FoodDeliveryService.Modules.Payments.Domain/"]
COPY ["src/Modules/Payments/FoodDeliveryService.Modules.Payments.IntegrationEvents/FoodDeliveryService.Modules.Payments.IntegrationEvents.csproj", "src/Modules/Payments/FoodDeliveryService.Modules.Payments.IntegrationEvents/"]
COPY ["src/Modules/Payments/FoodDeliveryService.Modules.Payments.Presentation/FoodDeliveryService.Modules.Payments.Presentation.csproj", "src/Modules/Payments/FoodDeliveryService.Modules.Payments.Presentation/"]
COPY ["src/Common/FoodDeliveryService.Common.Presentation/FoodDeliveryService.Common.Presentation.csproj", "src/Common/FoodDeliveryService.Common.Presentation/"]
COPY ["src/Modules/Users/FoodDeliveryService.Modules.Users.IntegrationEvents/FoodDeliveryService.Modules.Users.IntegrationEvents.csproj", "src/Modules/Users/FoodDeliveryService.Modules.Users.IntegrationEvents/"]
RUN dotnet restore "./src/API/FoodDeliveryService.Payments.Api/FoodDeliveryService.Payments.Api.csproj"
COPY . .
WORKDIR "/src/src/API/FoodDeliveryService.Payments.Api"
RUN dotnet build "./FoodDeliveryService.Payments.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./FoodDeliveryService.Payments.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FoodDeliveryService.Payments.Api.dll"]
