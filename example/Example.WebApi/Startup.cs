using Microsoft.OpenApi.Models;
using Jedi.ServiceFabric.AspNetCore.Correlation;
using Jedi.ServiceFabric.AspNetCore.OpenTelemetry;

namespace Example.WebApi
{
    public class Startup
    {
        
        private static NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

        public OpenTelemetryProvider OpenTelemetryProvider { get; set; }


        public Startup(IConfiguration configuration)
        {
            OpenTelemetryProvider = new OpenTelemetryProvider();
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<CorrelationIdDelegatingHandler>();

            //services.AddRazorPages();
            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "WebApplication1", Version = "v1" });
            });
            OpenTelemetryProvider.ConfigureOpenTelemetry(services);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseCorrelationId(new CorrelationIdOptions());

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "WebApplication1 v1"));
            }


            app.UseStaticFiles();

            app.UseRouting();


            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}