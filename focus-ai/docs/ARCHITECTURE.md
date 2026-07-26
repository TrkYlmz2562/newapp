# Mimari Notları

Bu belge, kodu okurken "neden böyle?" sorusunu doğuran kararları açıklar.
Kodun ne yaptığını kodun kendisi ve XML yorumları anlatır; burada yalnızca
gerekçeler var.

---

## 1. Katmanlar

```
FocusAI.Api  ──→  FocusAI.Infrastructure  ──→  FocusAI.Application  ──→  FocusAI.Domain
```

Ok yönü bağımlılığı gösterir. `Domain`'in hiçbir NuGet bağımlılığı yoktur.

**Neden Domain'de iş mantığı var?** Ürünün ayırt edici kararları — bir haberin
ne kadar güvenilir olduğu, günün özetine hangi haberlerin gireceği, hangi
haberlerin aynı olay olduğu — saf, deterministik fonksiyonlar olarak yazıldı.
Bunun pratik sonucu: 120 birim testinin çoğu veritabanı, HTTP ya da sahte nesne
olmadan çalışıyor ve tam olarak ürün davranışını sınıyor.

**Neden `IApplicationDbContext`?** Handler'lar somut `DbContext` yerine bu
arayüze bağlı. Böylece Application katmanı Npgsql ve pgvector'dan habersiz
kalıyor; sağlayıcıya özgü tek şey (`ILike` gibi) oraya sızmıyor.

---

## 2. Tekilleştirme neden üç aşamalı?

PRD §5.3 "aynı haber 20 sitede olabilir, tek haber oluştur" diyor. Tek bir
teknikle bunu yapmak ya çok pahalı ya çok yanlış:

| Aşama | Maliyet | Yakaladığı |
|---|---|---|
| `ContentHash` eşitliği | O(1) indeks araması | Birebir repost |
| SimHash Hamming mesafesi | 64-bit XOR + popcount | Hafif düzenlenmiş sendikasyon |
| Kosinüs benzerliği | Vektör okuması gerekir | Bağımsız yazılmış aynı olay |

Ucuzdan pahalıya sıralanır; çoğu çift ilk iki aşamada elenir.

**SimHash eşiği neden 10?** Tahminle değil, ölçümle. Seed'lenmiş kaynaklardan
gerçek başlıklarla ölçüldü:

| Durum | Hamming mesafesi |
|---|---|
| 15 kelimelik başlıkta tek kelime değişimi | 7–9 |
| Aynı olayın yeniden yazılmış hâli | ~19 |
| Alakasız haberler | 28–31 |
| Rastgele metin (teorik ortalama) | 32 |

10, ilk grubu yakalayıp ikinciyi embedding aşamasına bırakır. Bu bilinçli:
anlamı yargılayabilen tek araç embedding'dir.

**Neden aşırı birleştirmekten kaçınılıyor?** İki farklı haberi birleştirmek,
iki kart göstermekten çok daha kötü bir hatadır — birleşmiş kart, haberlerden
birini tamamen gizler. Bu yüzden kosinüs eşiği (0.86) yüksek tutuldu.

---

## 3. Güven skoru (PRD §5.4)

Beş PRD kriteri ağırlıklı bir toplama dönüşür:

| Bileşen | Ağırlık | Gerekçe |
|---|---|---|
| Resmi kaynak | 0.30 | Okuyucunun kendi doğrulayamayacağı sinyal |
| Kaç kaynak doğruladı | 0.25 | Aynı şekilde doğrulanamaz |
| Teknik doğruluk | 0.20 | Sürüm/benchmark/API adı var mı? |
| Topluluk güveni | 0.15 | Manipüle edilebilir, bu yüzden düşük |
| Güncellik | 0.10 | En zayıfı; sadece bayatlığı işaretler |

Bileşenler **ayrı ayrı saklanır ve arayüzde gösterilir**. Çıplak bir sayı sahte
kesinlik yaratır; skorun bütün değeri "neden bu skor?" sorusunun cevabında.

Doyma noktaları önemli: 8 kaynaktan sonra doğrulama puanı artmaz. Aksi hâlde
bir haber toplayıcı fırtınası, birinci elden bir duyuruyu sırf hacimle geçerdi.

---

## 4. Kişiselleştirme neden filtre balonu üretmiyor?

`PersonalizationScorer`'da global önem skorunun ağırlığı (0.45) ilgi alanı
eşleşmesinden (0.35) yüksek. Sonuç: kullanıcının hiç seçmediği bir konudaki
gerçekten büyük bir gelişme yine de akışa girer.

Sessize alınan konular ise **sert filtredir** — ağırlıklandırılmaz, elenir.
Kullanıcının açık "bunu görmek istemiyorum" beyanı bir sinyal değil, bir karardır.

`DigestComposer` ayrıca kategori başına en fazla 4 haber alır. Büyük bir model
duyurusu gününde saf sıralama, günlük özetin tamamını tek konuya çevirirdi;
bu da "günün bilmen gereken 10 konusu" vaadini bozardı. Boş kalan slotlar
taşan adaylardan doldurulur — eksik bir özet, sınırı aşan bir özetten kötüdür.

---

## 5. Anahtarsız çalışabilirlik

`docker compose up` hiçbir API anahtarı olmadan gerçek haberlerle dolu bir akış
üretir. Bunu iki yedek sağlar:

- **`HashingEmbeddingService`** — shingle'ları 1536 boyuta hash'ler. Semantik
  değildir, sözcük örtüşmesi yakalar. Amacı kümeleme ve "ilgili haberler"in
  çalışır kalmasıdır.
- **`ExtractiveFallback`** — model yoksa **üretmez, alıntılar**. Metinden
  cümle seçer, yorum yazmaz, `Confidence = 0.2` verir (arayüzün analiz bloğunu
  gizleme eşiğinin altında).

Bu ayrım kasıtlı: sistem hiçbir zaman "uydurulmuş analiz"i gerçek analiz gibi
sunmaz. Yedek moddayken bunu görünür kılar.

---

## 6. Kayda değer teknik kararlar

**`InvariantGlobalization=false` zorunlu.** Invariant modda ICU yüklenmez ve
iki şey sessizce bozulur: (1) Unicode ayrıştırma çalışmaz, yani "Açık" → "acik"
normalizasyonu olmaz ve Türkçe metinde tekilleştirme çöker; (2) yalnızca UTC
saat dilimi çözülür, yani her kullanıcının yerel 08:00 özeti UTC 08:00'e
kayar. İkisi de derlenir, çalışır ve yanlış sonuç verir — bu yüzden
`GlobalizationRegressionTests` bunları test altına aldı ve Dockerfile
`libicu`'yu açıkça kurar.

**UUIDv7 + istemci tarafı anahtar üretimi iki tuzak doğurdu:**

1. Slug'ın ayırt edici son eki UUID'nin *başından* alınıyordu; UUIDv7'nin ilk
   48 biti zaman damgasıdır, yani aynı partideki her slug çakışıyordu. Son ek
   artık UUID'nin sonundaki rastgele baytlardan alınıyor, ayrıca parti içi ve
   veritabanı çakışması ayrıca kontrol ediliyor.
2. Anahtar zaten dolu olduğu için EF, izlenen bir varlığın navigasyonuna
   eklenen yeni alt varlıkları `Added` değil `Modified` sayıyor ve var olmayan
   satıra `UPDATE` gönderiyordu. Bu yüzden `EnrichStories` içindeki
   `StorySummary`/`StoryAnalysis`/`StoryTrust`/`StoryTopic` açıkça `db.X.Add()`
   ile ekleniyor.

**Tüm `DateTimeOffset`'ler UTC'ye zorlanıyor.** Npgsql `timestamptz` için
sıfır olmayan offset kabul etmez; gerçek RSS akışları sürekli yerel offset
yayınlar ("-07:00"). Model düzeyinde bir dönüştürücü uygulandı ki gelecekte
eklenen hiçbir varlık bu hatayı yeniden getiremesin.

**Vektör araması ham SQL.** Domain, taşınabilirlik için embedding'leri
`float[]` tutar; EF değer dönüştürücüsü `vector` tipini LINQ'dan gizler, bu da
`<=>` operatörünün sorguya girmesini imkânsız kılar. Ham SQL bu yüzden dürüst
seçenektir — ve sıralamayı ait olduğu yerde, sunucuda tutar.

**HNSW indeksleri elle yazılmış bir migration'da.** EF Core'un model düzeyinde
HNSW kavramı yok. Bu indeksler olmadan her "ilgili haberler" sorgusu tam tablo
taramasıdır: bin satırda sorun değil, bir milyonda kullanılamaz.

**Rate limiting üç kademeli.** Genel 300/dk, kimlik doğrulama 10/dk (credential
stuffing hedefi), LLM uçları 20/dk (her istek gerçek para).

---

## 7. Test stratejisi

120 test, ağırlıklı olarak Domain'in saf mantığında. Öne çıkanlar:

- Skorlama sınırları (0–100 dışına çıkamaz), doyma davranışı, gelecek tarihli
  akışlar gibi kenar durumlar
- Kümelemede hem yakalama hem **yakalamama** (alakasız haberler birleşmemeli)
- Kişiselleştirmede sessize alma sert filtresi ve filtre balonu karşıtı davranış
- Gerçek hatalar için regresyon testleri: slug çakışması, ASCII olmayan
  User-Agent, globalization, SimHash eşiği

Testler üç gerçek hatayı yakaladı (globalization, SimHash eşiği, Türkçe
normalizasyon); uçtan uca canlı çalıştırma dört tanesini daha (ASCII
User-Agent, UTC offset, slug çakışması, EF izleme). Hepsi düzeltildi ve
regresyon testine bağlandı.
