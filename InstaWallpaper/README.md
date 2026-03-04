# InstaWallpaper

Instagram hesabındaki fotoğrafları otomatik slayt gösterisi olarak gösteren iOS uygulaması.

## Özellikler

- Public Instagram hesaplarından fotoğraf çekme
- Tam ekran slayt gösterisi
- Ayarlanabilir geçiş süresi (3 sn - 2 dk)
- 4 farklı geçiş efekti: Solma, Kaydırma, Yakınlaştırma, Çevirme
- Fotoğraf karıştırma
- Fotoğrafları Fotoğraflar'a kaydetme
- Ekranı açık tutma özelliği
- Arkaplan yenileme (saatte bir)

## Kurulum

1. Xcode 15+ gerekli (iOS 16+ hedef)
2. `InstaWallpaper.xcodeproj` dosyasını Xcode ile aç
3. Bundle ID'yi kendi Developer hesabınla güncelle: `com.personal.InstaWallpaper`
4. iPhone'una kur (TestFlight veya direkt kurulum)

## Duvar Kağıdı Değiştirme

iOS, uygulamaların duvar kağıdını otomatik değiştirmesine izin vermediği için:

1. Slayt gösterisinde istediğin fotoğrafa gel
2. Kaydet (↓) butonuna bas → Fotoğraflar'a kaydedilir
3. iOS Ayarlar > Duvar Kağıdı > Fotoğraf Seç

**Otomatik değiştirme için iOS Kısayollar:**
- Kısayollar uygulamasında "Otomasyon" oluştur
- Tetikleyici: Belirli saatler
- İşlem: Duvar kağıdını değiştir (Fotoğraflar albümünden)

## Teknik Notlar

- Instagram public profil scraping kullanır (picuki.com yedek olarak)
- NSCache tabanlı resim önbellekleme
- Swift concurrency (async/await, actors)
- iOS 16+ minimum gereksinim
