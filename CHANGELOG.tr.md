# Değişiklik Günlüğü

🇬🇧 [English](CHANGELOG.md) · 🇹🇷 **Türkçe**

Bu projedeki önemli değişikliklerin tümü bu dosyada belgelenir.
Biçim [Keep a Changelog](https://keepachangelog.com/tr-TR/1.1.0/) temel alınarak hazırlanmıştır.

## [Yayımlanmamış]

### Eklenenler

- Aracın müsaitlik takvimi: bir ayın her günü veritabanının bugününe göre boş, dolu, bakımda veya geçmiş; durum her
  hücrede yazılı (müşteriler kiralamaya açık araçlar için, yöneticiler her araç için).
- Yöneticiler için bakım ve hasar kayıtları (migration `AddMaintenanceLog`, `AracBakim` tablosu): kayıtlar aracın
  günlerini kapatır, aktif bir kiralamanın günlerini alamaz, silinmez iptal edilir ve veritabanının saatiyle
  damgalanır. Başka bir yöneticinin değişikliğinden önce yüklenmiş formun kaydı reddedilir ve kayıt yeniden yüklenir.
- Yöneticiler için aylara veya günlere ve araçlara göre gelir raporu; formüle karşı güvenli CSV olarak kaydedilir.
- Kiracı ve yöneticiler için yazdırılabilir HTML kiralama sözleşmesi; HTML kodlamalı ve sıkı bir
  Content-Security-Policy altında.
- Hesapların oturum sürümü (migration `AddSessionVersion`, `OturumSurumu` sütunu).
- Araçların sürümü (migration `AddCarVersion`, `Surum` sütunu): başka bir yöneticinin değişikliğinden önce yüklenmiş
  bir formdan gelen araç değişikliği reddedilir ve araç yeniden yüklenir.
- Testler: eski hesap ve araç formları (yazma içinde de karar verilen ve SQL Server'da yarışan), diğer oturumları
  kapatan kendi şifre değişikliği, kiralama sonlandırmanın kilit sırası ve SQL Server'da kilitlenme kurbanı, beklenen
  kilitten sonra damgalanan bakım zamanları, çakışan bakım kayıtları, korunan bakım girdisi, veritabanının ayı ve
  rapor aralığı, hızlı takvim tıklamaları, müşterilerden gizlenen geçmiş günler, kullanıcı adı içermeyen sözleşme,
  bugüne kadar sayılan gelir ve büyük yazı tipinde sözleşme düğmesi.
- Testler: bakımın kapattığı günler ve eski sürümler, takvim durumları, gelir toplamları ve sınırları, CSV formül
  enjeksiyonu, sözleşmenin kodlanması, politikası ve sahiplik denetimi, biten oturumlar; SQL Server üzerinde
  kiralama ile bakımın, aynı kaydı kaydeden iki yöneticinin ve yetkisi alınan yöneticinin bakım yazmasının yarışları,
  kısa komut zaman aşımında meşgul araç kilidi; kısıtlı oturum tüm yeni özellikleri çalıştırır.
- `%LOCALAPPDATA%\AracKiralama\logs` altında Serilog ile günlük dönen log dosyaları: 30 günden eski dosyalar silinir
  ve en fazla 30 dosya tutulur (testler bunu tarihli dosyalarla denetler). Hata kayıtları yalnızca istisna
  türlerini, HResult kodlarını ve metot adlarını içerir.
- Log dosyasında anonim denetim kayıtları: yalnızca sabit bir kod ve bir rol; asla bir hesap, araç veya tarih yok.
  Boş alanlı olanlar dahil her reddedilen giriş rolsüz aynı kodu yazar; yeni bir hesap ve alınmış değerlerle yapılan
  kayıt denemesi aynı nötr kodu yazar, `auth.account.registration_submitted`.
- Giriş, kayıt, şifre değiştirme, ilk yönetici, yöneticinin hesap formu, araç formu ve kiralama günleri ile tarihleri
  için merkezi FluentValidation doğrulayıcıları; kurallar, eski hesap ve araçların korunan değerleri ve Türkçe
  mesajlar değişmedi.
- Daha fazla Moq ve SQLite testi: doğrulayıcı sınırları ve mesaj sırası, en kısa ve en uzun kiralama, kiralama
  ufku, gece yarısını geçen teklif, güne göre iptal veya iade, kilit süresi, eski hesabın korunan değerleri,
  hiçbir şey yazmayan reddedilmiş girdi, denetim kodları ve log dosyalarının kişisel veri, kurulum anahtarı ya da
  hesapları veya kiralamaları ayırt ettiren sonuç içermediğini doğrulayan testler.
- Mimari diyagramı, eski veritabanı yükseltmesi ve ilk kurulumla İngilizce ve Türkçe README, tehdit modeli
  (`docs/THREAT_MODEL.md`) ve bu değişiklik günlüğü.
- CI'da sürüm işi: `v*` etiketlerinde etiket adı denetlenir, win-x64 derlemesi yayımlanır ve README'ler, değişiklik
  günlükleri ve `docs/*.sql` ile birlikte zip çıktısı olarak yüklenir.

### Değişenler

- Dolu günler yüzünden reddedilen kiralama artık aracın günlerin bir kısmında müsait olmadığını söyler; kiralama
  veya bakım için aynı cevap. Bakım geçmişi olan araç da silinemez.
- Hatalar izleme çıktısı yerine log dosyasına yazılır; hata pencereleri değişmedi. Reddedilen işlemler kendi
  başlarına log kaydı bırakmaz.
- README'deki eski uygulamaya ait ifadeler (.NET Framework, ADO.NET) güncel teknolojilerle değiştirildi.
- Gelir raporu yalnızca bugüne kadar teslim alınmış kiralamaları sayar (sonrakiler hâlâ ücretsiz iptal edilebilir),
  aralığın yalnızca bir kısmını kapsayan ayı günleriyle adlandırır ve bunu ekranda ve CSV'de bir notla belirtir.
- Takvim veritabanının bugününün ayıyla açılır; rapor bu aydan o güne kadarki aralıkla başlar. Müşteriler bugünden
  önceki her günü geçmiş olarak görür. Ay düğmelerine hızlı tıklamalar toplanır.
- Reddedilen bakım kaydı yöneticinin yazdıklarını korur; yalnızca başka bir yerde değiştirilen veya iptal edilen kayıt
  yeniden yüklenir. Bir bakım kaydı aracın başka bir aktif kaydıyla gün paylaşamaz ve zamanları, aracın kilidinden
  sonra yazıldığı anda alınır.
- Kiralama sözleşmesi artık kullanıcı adını yazdırmaz; kiracı e-posta adresi ve telefonuyla belirtilir.
- Kendi rolünü veya şifresini değiştiren yönetici, sonraki her tıklamada reddedilmek yerine bir açıklamayla oturumdan
  çıkarılır.
- Kiralama kartındaki sözleşme düğmesi boyutunu yazı tipinden alır; kart yüksek karşıtlıkta sistem renklerini korur.

### Kaldırılanlar

- Artık var olmayan ekranları gösteren eski uygulamanın ekran görüntüleri; tasarım güncellemesi yenilerini getirecek.

### Güvenlik

- Rol değişikliği veya yöneticinin belirlediği şifre, hesabın tüm açık oturumlarını değişiklik sonradan geri alınsa
  bile kalıcı olarak sonlandırır; her yazma işlemi oturum sürümünü, rolü ve hesabı kendi transaction'ı içinde denetler.
- Hesap değişikliği formunun yüklediği oturum sürümünü taşır; böylece eski bir form, başka bir yöneticinin az önce
  aldığı yönetici rolünü geri veremez.
- Hesabın kendi şifre değişikliği önce oturumu denetler, oturum sürümünü artırır ve böylece hesabın diğer tüm
  oturumlarını (örneğin çalınan şifreyle açılmış olanı) kapatır; şifreyi değiştiren pencere çalışmaya devam eder.
- Kiralamayı sonlandırma, işlemi yapan hesabı denetlemeden önce aracın kilidini alır (diğer araç yazmalarıyla aynı
  sıra); kilitlenme kurbanı meşgul mesajıyla cevaplanır.
- Oturum sürümü, oturum, işlem yapan hesap veya giriş verisi oluşturulan her yerde zorunlu bir değerdir.
- Kilit isteği her zaman kilidin bekleme süresinden uzun bir komut zaman aşımı alır; meşgul bir kilit hiçbir şey
  yazmadan meşgul cevabıyla biter, komut zaman aşımıyla değil.
- Uygulamanın oturumu bakım kayıtlarında `DELETE` yetkisi almaz (`docs/grant-app-login.sql`).
- Kullanıcı adları, e-posta adresleri, telefon numaraları, şifreler, kurulum anahtarları, araçlar, plakalar, tutarlar
  ve istisna mesajları hiçbir zaman loga yazılmaz.

## Temel

Bu değişiklik günlüğüne başlanmadan önce birleştirilen, eski uygulamanın yeniden yazımı.

### Eklenenler

- EF Core, bağımlılık enjeksiyonu ve otomatik migration'larla katmanlı .NET 10 çözümü (Domain, Business, Data,
  UI); eski veritabanı verileri korunarak yerinde yükseltilir.
- Müşterinin seçtiği başlangıç günüyle yarı açık gün aralıkları olarak kiralamalar; ayarlanabilir en kısa ve en uzun
  kiralama, kiralama ufku ve uzun kiralama indirimi; satır silmeden alış gününden önce iptal ve kiralama başladıktan
  sonra iade; katalog ve yöneticinin kiralama listesi için filtreler.
- Boş veritabanı için ilk kurulum; hesapları olan ama yöneticisi olmayan veritabanı için yalnızca veritabanı
  sahibinin `docs/issue-setup-key.sql` ile ürettiği tek kullanımlık kurulum anahtarı ve uygulamanın oturumu için
  `docs/grant-app-login.sql` içinde örnek yetkiler.
- Varsayılan olarak kapalı, hayali araçlarla ve hesapsız isteğe bağlı demo modu (`ARAC_DEMO`, `Seed:Demo`).
- Üçüncü taraf görseller yerine özgün çizimler ve yalnızca herkese açık adreslerle sınırlı araç resmi indirme.
- SQLite üzerinde servis testleri ve SQL Server üzerinde kiralama yarışları, kurulum anahtarları, kısıtlı oturum ve
  eski veritabanı yükseltmesi testleri; SQL Server işi olan CI.

### Güvenlik

- Düz metin şifrelerin bir sonraki girişte yükseltildiği PBKDF2 şifre özetleri; 5 hatalı denemeden sonra 15 dakika
  kilit, her hata için aynı cevap, veritabanının saatiyle.
- Her serviste rol ve sahiplik denetimi; işlemi yapan hesabın rolü her işlemde veritabanından okunur ve her yetkili
  yazmanın içinde yeniden denetlenir; başka bir hesabın kiralaması var olmayan bir kiralamayla aynı cevabı alır.
- Çakışma denetimi aynı işlemde olacak şekilde araç başına kilit altında kiralamalar; bir araç aynı gün için
  hiçbir zaman iki kez kiralanmaz.
- Varsayılan kurulumda bilinen şifreli hesap yok.
