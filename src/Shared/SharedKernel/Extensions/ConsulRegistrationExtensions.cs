using Consul;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Extensions;

public static class ConsulRegistrationExtensions
{
    public static IServiceCollection AddConsulConfig(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IConsulClient, ConsulClient>(_ => new ConsulClient(cfg =>
        {
            var address = configuration["Consul:Host"] ?? "http://localhost:8500";
            cfg.Address = new Uri(address);
        }));

        return services;
    }

    public static IApplicationBuilder UseConsulServiceRegistration(
        this IApplicationBuilder app,
        IConfiguration configuration,
        IHostApplicationLifetime lifetime)
    {
        var serviceName = configuration["Consul:ServiceName"];
        var servicePortString = configuration["Consul:ServicePort"];

        if (string.IsNullOrWhiteSpace(serviceName) || !int.TryParse(servicePortString, out var servicePort))
        {
            return app;
        }

        var consulClient = app.ApplicationServices.GetService<IConsulClient>();
        var logger = app.ApplicationServices.GetService<ILoggerFactory>()?.CreateLogger("ConsulRegistration");

        if (consulClient == null)
        {
            return app;
        }

        var serviceId = $"{serviceName}-{Guid.NewGuid()}";
        var host = configuration["Consul:ServiceHost"] ?? "localhost";

        var registration = new AgentServiceRegistration
        {
            ID = serviceId,
            Name = serviceName,
            Address = host,
            Port = servicePort,
            Check = new AgentServiceCheck
            {
                HTTP = $"http://{host}:{servicePort}/health",
                Interval = TimeSpan.FromSeconds(10),
                Timeout = TimeSpan.FromSeconds(3),
                DeregisterCriticalServiceAfter = TimeSpan.FromMinutes(1)
            }
        };

        try
        {
            consulClient.Agent.ServiceRegister(registration).GetAwaiter().GetResult();
            logger?.LogInformation("Đã đăng ký dịch vụ {ServiceName} (ID: {ServiceId}) vào Consul tại cổng {Port}", serviceName, serviceId, servicePort);

            lifetime.ApplicationStopping.Register(() =>
            {
                try
                {
                    consulClient.Agent.ServiceDeregister(serviceId).GetAwaiter().GetResult();
                    logger?.LogInformation("Đã hủy đăng ký dịch vụ {ServiceId} khỏi Consul", serviceId);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Lỗi khi hủy đăng ký dịch vụ {ServiceId} khỏi Consul", serviceId);
                }
            });
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Không thể kết nối tới Consul Agent (:8500). Dịch vụ vẫn chạy độc lập bình thường.");
        }

        return app;
    }
}

