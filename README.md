# 🚗 Araç Kiralama Otomasyonu

Bu proje, C# (Windows Forms) ve SQL Server kullanılarak geliştirilmiş basit bir **Araç Kiralama Otomasyonu**dur. Proje, kullanıcıların araç kiralayabildiği, araç listesini görüntüleyebildiği ve veritabanı üzerinde temel CRUD (Create, Read, Update, Delete) işlemlerini gerçekleştirebildiği bir yapıya sahiptir.

## 🚀 Özellikler

- 🚘 Araç listesi görüntüleme  
- ➕ Yeni araç ekleme  
- ✏️ Araç bilgilerini güncelleme ve silme  
- 👤 Müşteri kayıt işlemleri  
- 📅 Araç kiralama ve teslim işlemleri  
- 🔌 SQL Server ile bağlantı

## 🛠️ Kullanılan Teknolojiler

- **C# (Windows Forms)**
- **.NET Framework**
- **SQL Server**
- **ADO.NET** (SqlConnection, SqlCommand vs.)

## 🔧 Veritabanı Bağlantısı

Bağlantı dizesi koda yazılmaz. Uygulama onu sırasıyla `appsettings.json` (sürüm kontrolüne girmez), kullanıcı gizli ayarları (user secrets) ve `ARAC_CONNECTION` ortam değişkeninden okur; sonraki kaynak öncekini geçersiz kılar.

```powershell
# Seçenek 1: örnek ayar dosyasını kopyalayıp bağlantı dizesini düzenleyin
Copy-Item src\AracKiralama.UI\appsettings.example.json src\AracKiralama.UI\appsettings.json

# Seçenek 2: kullanıcı gizli ayarları
dotnet user-secrets set "ConnectionStrings:AracKiralama" "Server=YOUR_SERVER_NAME;Database=AracKiralama;Trusted_Connection=True;TrustServerCertificate=True;" --project src\AracKiralama.UI

# Seçenek 3: ortam değişkeni
$env:ARAC_CONNECTION = "Server=YOUR_SERVER_NAME;Database=AracKiralama;Trusted_Connection=True;TrustServerCertificate=True;"
```

> ❗ `YOUR_SERVER_NAME` kısmını kendi SQL Server örneğinizin adıyla değiştirin (örneğin `localhost\SQLEXPRESS`).

## 📌 Kurulum

1. Projeyi klonlayın ve [.NET 10 SDK](https://dotnet.microsoft.com/download) kurulu olduğundan emin olun.
2. Bağlantı dizesini yukarıdaki yollardan biriyle verin.
3. Derleyip çalıştırın: `dotnet run --project src\AracKiralama.UI`. Veritabanı ilk açılışta EF Core migration'larıyla oluşturulur; eski uygulamanın veritabanı verileri korunarak yerinde yükseltilir. Migration'ları elle uygulamak için: `dotnet tool restore` ve `dotnet ef database update --project src\AracKiralama.Data`.
4. Varsayılan ya da belgelenmiş şifreli bir hesap yoktur. Boş bir veritabanında ilk açılışta yönetici hesabını kendi belirlediğiniz şifreyle oluşturursunuz. Hesapları olup yöneticisi olmayan bir veritabanında ilk yönetici, veritabanı sahibinin `docs\issue-setup-key.sql` betiğiyle ürettiği kurulum anahtarıyla oluşturulur. Uygulamanın kendi SQL oturumu için örnek yetkiler `docs\grant-app-login.sql` dosyasındadır.
5. Deneme verisi (yalnızca hayali araçlar, hesap yok) yalnızca istenirse ve yalnızca boş veritabanına eklenir: `ARAC_DEMO=1` ya da `"Seed": { "Demo": true }`.
6. Testler: `dotnet test -c Release`. SQL Server testleri varsayılan olarak `localhost\SQLEXPRESS` kullanır (`ARAC_TEST_SQLSERVER` ile değiştirilebilir); `ARAC_SKIP_SQL_TESTS=1` bu testleri atlar.
## 📸 Ekran Görüntüleri

### Giriş Ekranı  
![Ana Ekran](screenshots/login.png)

### Kiralanabilir Araçlar  
![Araç Listesi](screenshots/homepage.png)

### Araç Kiralama Ekranı  
![Araç Kiralama](screenshots/rent.png)

### Kiralanan Aracı İptal Etme (Kullanıcı)  
![Kiralamayı İptal Etme](screenshots/user_rent_cancel.png)

### Admin Paneli  
![Araç Bilgilerini Görüntüleme ve Güncelleme](screenshots/admin_panel.png)

### Admin Kullanıcı Yönetimi  
![Kullanıcı Bilgilerini Görüntüleme ve Güncelleme](screenshots/admin_panel2.png)

### Admin Kiralanan Araçlar Yönetimi  
![Kiralanan Araçları Görüntüleme ve İptal Etme](screenshots/admin_rent_cancel.png)

---

Teşekkürler! 😊 Projeyi faydalı bulduysanız yıldız bırakmayı unutmayın. Geliştirme sürecine katkıda bulunmak isterseniz PR (Pull Request) gönderebilirsiniz.
