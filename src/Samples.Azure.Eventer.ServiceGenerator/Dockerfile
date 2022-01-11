FROM mcr.microsoft.com/dotnet/core/runtime:3.1.1-alpine AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/core/sdk:3.1.101-alpine AS build
WORKDIR /src
COPY ["Samples.Azure.Eventer.ServiceGenerator.csproj", "Samples.Azure.Eventer.ServiceGenerator/"]
RUN dotnet restore "Samples.Azure.Eventer.ServiceGenerator/Samples.Azure.Eventer.ServiceGenerator.csproj"
WORKDIR "/src/Samples.Azure.Eventer.ServiceGenerator"
COPY . .
RUN dotnet build "Samples.Azure.Eventer.ServiceGenerator.csproj" -c Release -o /app

FROM build AS publish
RUN dotnet publish "Samples.Azure.Eventer.ServiceGenerator.csproj" -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "Samples.Azure.Eventer.ServiceGenerator.dll"]
