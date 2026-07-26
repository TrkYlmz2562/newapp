# Yol Haritası — Durum

PRD §17'deki kapsamın bu depodaki karşılığı.

## MVP (6–8 hafta) — tamamlandı

| PRD maddesi | Durum | Nerede |
|---|---|---|
| Kullanıcı girişi | ✅ | JWT + rotasyonlu refresh token, PBKDF2 (210k iterasyon) |
| RSS toplama | ✅ | 35 kaynak, 4 adaptör (RSS/Atom, Hacker News, Reddit, GitHub) |
| AI özetleri | ✅ | 7 sağlayıcı + extractive yedek |
| Haber detay sayfası | ✅ | `/story/[slug]` — özet, güven kırılımı, AI yorumu, kaynaklar |
| Kaydetme | ✅ | `/bookmarks`, not ve etiket desteğiyle |
| Arama | ✅ | Doğal dil ayrıştırma + Meilisearch ya da token tabanlı DB araması |
| PWA | ✅ | Manifest, service worker, çevrimdışı sayfa, kurulabilir |
| Günlük özet | ✅ | Kullanıcının yerel saatine göre, kategori çeşitliliği kuralıyla |

MVP dışında ayrıca tamamlananlar:

| Özellik | PRD | Durum |
|---|---|---|
| Güven skoru | §5.4 | ✅ 5 bileşen, kırılım arayüzde |
| AI yorumu (abartılıyor mu, kalıcı mı) | §5.5 | ✅ |
| Bana özel sıralama | §5.6 | ✅ |
| Haftalık özet | §5.8 | ✅ 20 haber |
| Aylık trend | §5.9 | ✅ Grafiklerle |
| Bildirim tercihleri | §10 | ⚠️ Ayarlar ve zamanlama tam; gönderim yok |
| Gamification | §15 | ✅ Seri, rozet, ilerleme — kasıtlı olarak sade |
| Soru-cevap (RAG) | §19 | ✅ `/api/ask` |

---

## v1.1

| Madde | Not |
|---|---|
| GitHub analizleri | Adaptör var; kimlik doğrulamalı token gerekiyor |
| YouTube özetleri | RSS akışı toplanıyor; transkript özeti yok |
| Haftalık rapor | ✅ tamamlandı |
| Öğrenme önerileri | ✅ tamamlandı (LLM sağlayıcısı gerektirir) |

## v1.2

AI Agent, sesli özet, podcast modu, takvim entegrasyonu — başlanmadı.

## v2

Çok kullanıcılı çalışma alanı, kurumsal kullanım, genel API, tarayıcı
eklentisi, macOS ve native mobil uygulamalar — başlanmadı.

---

## Sıradaki iş için öneriler

Kod tabanının şu an en çok fayda getirecek eklemeleri, önem sırasıyla:

1. **Web Push gönderimi.** İstemci tarafı (service worker `push` ve
   `notificationclick` işleyicileri), abonelik saklama, sessiz saatler ve
   zamanlanmış iş hazır. Eksik olan tek şey VAPID imzalayan bir gönderici;
   `IPushNotifier` implementasyonunu değiştirmek yeterli.
2. **GitHub ve Reddit için token.** İkisi de kimliksiz istekleri engelliyor.
   Kaynak başına kimlik bilgisi alanı eklemek, PRD §6'daki topluluk
   kaynaklarının yarısını geri kazandırır.
3. **Entegrasyon testleri.** `Program` sınıfı `WebApplicationFactory` için
   `public partial` bırakıldı; Testcontainers ile Postgres+pgvector kaldırıp
   uçtan uca hattı test etmek doğal sonraki adım.
4. **Premium katmanı (§16).** `SubscriptionPlan` ve `AiUsageLog` altyapısı
   yerinde; kota uygulaması ve ödeme entegrasyonu eksik.
