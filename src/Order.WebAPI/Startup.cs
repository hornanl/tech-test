using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Order.Data;
using Order.Service;
using OrderService.WebAPI.ExceptionHandlers;

namespace OrderService.WebAPI
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<OrderContext>(options =>
            {
                var serviceOptions = Configuration["OrderConnectionString"];
                options
                // Do not track read-only results. Update queries enable tracking when needed.
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                .UseLazyLoadingProxies()
                .UseMySQL(serviceOptions);
            });

            services.AddScoped<IOrderService, Order.Service.OrderService>();
            services.AddScoped<IOrderRepository, OrderRepository>();

            services.AddControllers();
            services.AddProblemDetails();
            services.AddExceptionHandler<OrderDataExceptionHandler>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // Catch errors from the endpoints and pass them to the registered exception handler.
            app.UseExceptionHandler();

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
