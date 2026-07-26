# PRD — Focus AI

**Yapay Zeka Destekli Teknoloji Takip Platformu**

| | |
|---|---|
| Versiyon | 1.0 |
| Durum | Taslak |
| Platform | PWA · iOS Safari (Ana Ekrana Ekle) · Android · Masaüstü |

> Bu belge ürünün kaynak spesifikasyonudur. Implementasyonun bu maddelere
> karşılık gelen durumu için [ROADMAP.md](./ROADMAP.md)'ye bakın.

---

## 1. Vizyon

Focus AI; sosyal medya yerine kullanılabilecek, dikkat dağıtmayan, yapay zekâ
destekli bir bilgi platformudur.

Amaç: kullanıcının internette saatlerce dolaşmasına gerek kalmadan, yalnızca
gerçekten önemli olan gelişmeleri sunmaktır.

Platform; teknoloji, yapay zeka, yazılım, bilim, startup, açık kaynak ve
kişisel gelişim alanlarındaki bilgileri onlarca farklı kaynaktan toplar.

Yapay zekâ tekrar eden haberleri birleştirir, gereksiz haberleri eler,
kullanıcının ilgi alanına göre sıralar, özet çıkarır, yorum yapar ve öğrenme
önerileri verir.

## 2. Hedef Kitle

**Birincil:** Yazılım mühendisleri · AI geliştiricileri · Full Stack Developer ·
DevOps · CTO · Teknik ekip liderleri

**İkincil:** Üniversite öğrencileri · Girişimciler · Teknoloji meraklıları

## 3. Problem

Bilgi şu kaynaklara dağılmış durumda: X, Reddit, LinkedIn, Hacker News, GitHub,
Product Hunt, arXiv, Medium, Dev.to, YouTube, OpenAI Blog, Anthropic, Google AI,
Microsoft, Meta AI.

Sonuç: bilgi tekrar ediyor, clickbait oluşuyor, zaman kaybı yaşanıyor, önemli
gelişmeler kaçıyor.

## 4. Çözüm

Kullanıcı yalnızca uygulamayı açar. Yapay zekâ 1000+ içeriği analiz eder.
Kullanıcıya yalnızca **"Günün Bilmen Gereken 10 Konusu"** gösterilir.

---

## 5. Temel Özellikler

### 5.1 Günlük Özet

Her gün 08:00'de hazır olur. İçerik: en önemli AI gelişmesi, yazılım,
teknoloji, açık kaynak, kariyer, araçlar. Okuma süresi 5 dakika.

### 5.2 AI Özeti

Her haber için: Başlık · Kısa özet · Neden önemli? · Kimleri etkiliyor? ·
Ben ne yapmalıyım? · Kaynaklar

**Örnek**

> GPT-6 yayınlandı.
>
> **AI Analizi:** Eğer .NET geliştiriyorsan Function Calling performansı önemli
> ölçüde gelişti. Eski projelerini değiştirmen gerekmiyor. Yeni projelerde
> kullanılması önerilir.

### 5.3 Kaynak Birleştirme

Aynı haber 20 farklı sitede olabilir. Sistem tek haber oluşturur, altında
kaynakları gösterir.

### 5.4 Güven Skoru

Her haber 0–100 arası puanlanır. Kriterler: resmi kaynak · kaç kaynak
doğruladı · tarih · teknik doğruluk · topluluk güveni.

### 5.5 AI Yorumu

Her haber için AI şunları yazar: Bu neden önemli? · Gerçek etkisi nedir? ·
Abartılıyor mu? · Ne zaman öğrenilmeli? · Bu teknoloji ölür mü?

### 5.6 Bana Özel

Profil oluşturulur. Örneğin ilgi alanları: C#, .NET, Angular, SQL, Docker,
Azure, Yapay Zeka, Mobil. AI haberleri buna göre sıralar.

### 5.7 Öğrenme Modu

AI günlük 15 dakika öğrenilecek konu önerir.

> Bugün MCP öğren. **Neden?** Çünkü Agent ekosisteminde artık standart olmaya
> başladı.

### 5.8 Haftalık Özet

Haftanın en önemli 20 gelişmesi, tek ekranda.

### 5.9 Aylık Trend

Bu ay en çok konuşulan teknolojiler, grafiklerle.

---

## 6. İçerik Kaynakları

**Resmi Bloglar:** OpenAI · Anthropic · Google DeepMind · Microsoft · AWS ·
Cloudflare · Vercel · GitHub Blog · Docker · JetBrains · Apple Developer ·
Android Developers · Meta AI · NVIDIA

**Topluluk:** Hacker News · Reddit · Dev.to · Medium · Product Hunt

**Kod:** GitHub Trending · Awesome Lists · GitHub Releases

**Akademik:** arXiv · Papers With Code · Nature · MIT News

**Video:** YouTube RSS · Conference kanalları (Google IO, WWDC, Build, NDC)

## 7. Ana Sayfa

**Üst:** Arama · Bugünün Özeti

**Kartlar:** 🔥 Günün En Önemlisi · 🤖 AI · 💻 Yazılım · 🚀 Startup ·
📚 Öğren · 📈 Trendler

**Alt Menü:** Home · Explore · Bookmarks · Learning · Profile

## 8. Haber Detayı

Başlık · Özet · AI Yorumu · Kaynaklar · İlgili Haberler · Video · Github ·
Makale · Yorum

## 9. Arama

Doğal dil destekli.

> "Son bir ayda çıkan tüm AI Agent haberlerini göster."

## 10. Bildirimler

Sabah Özeti · Akşam Özeti · Sadece Büyük Haberler · Haftalık Özet

## 11. AI Özellikleri

LLM provider bağımsız. Destek: OpenAI · Anthropic · Gemini · OpenRouter ·
Yerel Model (Ollama, vLLM, LM Studio)

## 12. Teknoloji

| Katman | Seçim |
|---|---|
| Frontend | Next.js · React · TypeScript · Tailwind · PWA |
| Backend | .NET 9 Web API · Clean Architecture · CQRS · MediatR · EF Core |
| Database | PostgreSQL · Redis · pgvector |
| Background Jobs | Hangfire · Quartz · Temporal (opsiyonel) |
| Search | Meilisearch veya Typesense |
| Queue | RabbitMQ veya Kafka |
| Storage | S3 · Cloudflare R2 |
| Deployment | Docker · Kubernetes (opsiyonel) · GitHub Actions · Cloudflare CDN |

## 13. AI Pipeline

```
RSS → Crawler → Normalize → Duplicate Detection → Embedding → Vector Database
    → LLM Summary → Importance Score → Personalization → Frontend
```

## 14. Kullanıcı Profili

İlgi Alanları · Okunan Haberler · Kaydedilenler · Öğrenme Seviyesi ·
Favori Kaynaklar · Sessize Alınan Konular

## 15. Gamification

Günlük Seri · Haftalık Hedef · Tamamlanan Öğrenmeler · Rozetler ·
İlerleme Grafiği

> **Not:** Bu sistem dikkat bağımlılığı oluşturmayacak şekilde sade
> tasarlanmalıdır.

## 16. Premium

Daha uzun özetler · Sınırsız AI soru-cevap · PDF raporları · Özel araştırmalar ·
Takım çalışma alanı · Şirket dashboard'u

## 17. Yol Haritası

**MVP (6–8 Hafta):** Kullanıcı girişi · RSS toplama · AI özetleri · Haber
detay sayfası · Kaydetme · Arama · PWA · Günlük özet

**v1.1:** GitHub analizleri · YouTube özetleri · Haftalık rapor · Öğrenme
önerileri

**v1.2:** AI Agent · Sesli özet · Podcast modu · Takvim entegrasyonu

**v2:** Çok kullanıcılı çalışma alanı · Kurumsal kullanım · API · Browser
Extension · macOS uygulaması · iOS ve Android native uygulamaları

## 18. Başarı Kriterleri (KPI)

- Günlük aktif kullanıcı (DAU)
- Haftalık aktif kullanıcı (WAU)
- Ortalama günlük kullanım süresi (hedef: 10–15 dakika)
- Günlük okunan içerik sayısı
- Haftalık geri dönüş oranı (Retention)
- AI özetlerinin faydalı bulunma oranı
- Haberlerin okunma/tıklanma oranı
- Bildirim etkileşim oranı
- Premium dönüşüm oranı

## 19. Gelecek Vizyonu

Uzun vadede Focus AI, bir haber okuyucu olmaktan çıkıp **kişisel teknik
araştırma asistanına** dönüşmelidir.

Kullanıcı şu soruları doğal dille sorabilmeli:

- "Bu hafta .NET ekosisteminde ne değişti?"
- "Angular için önemli breaking change var mı?"
- "Son çıkan AI araçlarından hangisi benim projelerimde işime yarar?"
- "Bu teknolojiyi öğrenmeye değer mi?"
- "Son 3 ayda MCP hakkında neler oldu?"

Sistem; haberleri, blog yazılarını, GitHub aktivitelerini, akademik makaleleri
ve videoları birleştirerek tek bir güvenilir cevap üretebilmelidir.
