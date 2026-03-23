// Program.cs
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

// Добавление сервисов
builder.Services.AddRazorPages();
builder.Services.AddDistributedMemoryCache(); // Для сессии
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(1);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddHttpClient("ManagerClient", client =>
{
    client.BaseAddress = new Uri("http://localhost:8080");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 500 * 1024 * 1024; // 500 MB
    options.ValueLengthLimit = 256; // Для полей формы
    options.MultipartHeadersLengthLimit = 1024;
});
var app = builder.Build();

// Middleware
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession(); // Важно: до MapRazorPages
app.MapRazorPages();

app.Run();