# Focus AI

Yapay zekâ destekli teknoloji takip platformu. Onlarca kaynaktan toplanan
gelişmeleri birleştirir, özetler, güven skoru verir ve kullanıcının ilgi
alanlarına göre sıralar — sosyal medya yerine günde 5 dakika.

Bu depo, [PRD](./docs/PRD.md)'de tanımlanan MVP kapsamının çalışan
implementasyonudur.

---

## Hızlı başlangıç

```bash
cd focus-ai
docker compose up --build
```

- Web: <http://localhost:3000>
- API + Swagger: <http://localhost:5210/swagger>

> **Windows'ta çalıştırma.** Docker Desktop'ı kurun (WSL2 arka ucunu otomatik
> ayarlar), Git for Windows'u kurun, ardından PowerShell'de:
> `git clone -b claude/focus-ai-prd-35qcz9 https://github.com/TrkYlmz2562/newapp.git`
> → `cd newapp\focus-ai` → `docker compose up --build`. Sonra tarayıcıda
> `localhost:3000`. `.env` dosyası gerekmez — varsayılanlar çalışır. Repoda
> `.gitattributes` satır sonlarını LF'ye zorladığı için build Windows'ta da
> aynı çalışır.

**API anahtarı gerekmez.** Bir LLM sağlayıcısı tanımlı değilse sistem yerel
hash tabanlı embedding ve extractive (üretmeyen, alıntılayan) özet moduna
düşer; ingestion → kümeleme → skorlama → yayınlama hattının tamamı yine çalışır
ve gerçek haberler gelir. AI özeti ve yorum katmanı için sağlayıcı ekleyin:

```bash
LLM_PROVIDER=OpenAI OPENAI_API_KEY=sk-... \
EMBEDDINGS_PROVIDER=OpenAI \
docker compose up --build
```

### İlk içeriği getirme

Arka plan işleri 20 dakikada bir çalışır. Beklemeden doldurmak için bir admin
kullanıcı oluşturup hattı elle tetikleyebilirsiniz:

```bash
# 1) Kayıt ol
curl -X POST http://localhost:5210/api/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"email":"dev@focus.ai","password":"FocusAI2026!","displayName":"Dev",
       "interestSlugs":["dotnet","angular","ai","mcp","docker","azure"]}'

# 2) Admin rolü ver
docker compose exec postgres psql -U focusai -d focusai \
  -c "UPDATE users SET \"Roles\" = ARRAY['user','admin'] WHERE \"NormalizedEmail\"='dev@focus.ai';"

# 3) Giriş yapıp token al, sonra tüm hattı çalıştır
curl -X POST http://localhost:5210/api/admin/pipeline -H "Authorization: Bearer $TOKEN"
```

---

## Mimari

```
focus-ai/
├── backend/                     .NET 9 · Clean Architecture · CQRS + MediatR
│   ├── src/FocusAI.Domain/         Entity'ler, skorlama, kümeleme, metin işleme
│   ├── src/FocusAI.Application/    Use case'ler (komut/sorgu), port arayüzleri
│   ├── src/FocusAI.Infrastructure/ EF Core + pgvector, LLM istemcileri, adaptörler
│   ├── src/FocusAI.Api/            Minimal API uçları, JWT, Hangfire işleri
│   └── tests/FocusAI.UnitTests/    120 test
├── frontend/                    Next.js 15 · React 19 · TypeScript · Tailwind · PWA
└── docker-compose.yml           Postgres+pgvector · Redis · Meilisearch · API · Web
```

Bağımlılık yönü tek yönlüdür: `Api → Infrastructure → Application → Domain`.
Domain katmanının hiçbir dış bağımlılığı yoktur ve ürünün asıl kararlarını
(güven skoru, önem skoru, kişiselleştirme, kümeleme) saf fonksiyonlar olarak
barındırır — bu yüzden tamamı birim testiyle kapsanabiliyor.

Ayrıntı için [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md).

---

## AI hattı (PRD §13)

```
RSS/API  →  Crawler  →  Normalize  →  Duplicate Detection  →  Embedding
                                                                   ↓
Frontend  ←  Personalization  ←  Importance Score  ←  LLM Summary  ←  Vector DB
```

| Aşama | Nerede | Not |
|---|---|---|
| Toplama | `IngestSourcesCommand` | 35 kaynak, koşullu GET (ETag/If-Modified-Since), hatalarda üstel geri çekilme |
| Normalize | `UrlNormalizer`, `TextNormalizer` | Takip parametreleri temizlenir, Unicode ayrıştırılır |
| Tekilleştirme | `StoryClusterer` | 3 aşama: tam hash → SimHash (Hamming ≤ 10) → kosinüs benzerliği (≥ 0.86) |
| Embedding | `IEmbeddingService` | OpenAI uyumlu uç ya da yerel hash tabanlı yedek |
| Özet | `IContentAiService` | 7 sağlayıcı; başarısızlıkta extractive yedek |
| Çeviri | `IContentTranslator` | Opsiyonel; yalnızca modelin yazmadığı metni Türkçeleştirir |
| Güven skoru | `TrustScoreCalculator` | 5 bileşen, ağırlıklı; kırılım kullanıcıya gösterilir |
| Önem skoru | `ImportanceScoreCalculator` | Günlük özetin sıralamasını belirler |
| Kişiselleştirme | `PersonalizationScorer` | Sessize alınanlar sert filtre; gerisi ağırlıklı harman |

---

## LLM sağlayıcıları (PRD §11)

Sağlayıcı bağımsızdır; `appsettings.json` içindeki `Llm` bölümünden seçilir.

| Sağlayıcı | Protokol |
|---|---|
| OpenAI, OpenRouter, Ollama, vLLM, LM Studio | OpenAI uyumlu `chat/completions` |
| Anthropic | Messages API |
| Gemini | `generateContent` |
| _(tanımsız)_ | Extractive yedek — uydurmaz, alıntılar |

---

## Telefondan erişim ve HTTPS (çevrimdışı okuma bunun arkasında)

Tailscale IP'siyle `http://100.x.y.z:3000` çalışır ama **PWA katmanı tamamen
ölüdür**: servis worker'lar yalnızca güvenli bağlamda (HTTPS ya da `localhost`)
kaydolur, `'serviceWorker' in navigator` düz HTTP'de `false` döner. Yani
çevrimdışı okuma, ana ekrana ekleme ve bildirimler sessizce kapalıdır —
uygulama bunun dışında gayet sağlıklı göründüğü için fark edilmesi zordur.
Geliştirmede hiç görünmez, çünkü `localhost` muaf.

Çözüm kod değil taşıma katmanı. Tailscale kendi `*.ts.net` adına ücretsiz
sertifika veriyor; iki komut yetiyor (PC'de, `docker compose up` çalışırken):

```bash
tailscale serve --bg --set-path /     http://localhost:3000
tailscale serve --bg --set-path /api  http://localhost:5210/api
tailscale serve status          # https://<makine>.<tailnet>.ts.net
```

Frontend ve API tek adın altında toplandığı için **CORS devreden çıkıyor**.
Bunu kullanmak için `.env`:

```bash
NEXT_PUBLIC_API_URL=          # boş bırak = aynı köken
```

ve `docker compose up -d --build web` (bu değer derleme anında gömülüyor).

Sonra telefonda `https://<makine>.<tailnet>.ts.net` → Safari'de Paylaş →
"Ana Ekrana Ekle". Kaydettiğin haberler artık sinyalsiz açılır.

---

## Çeviri (opsiyonel, varsayılan kapalı)

LLM tanımlı değilken hat, makalenin kendi cümlelerini çıkarıp yayınlar. İngilizce
bir kaynakta bu, Türkçe okura İngilizce kart demektir. Bu katman o yolu
Türkçeleştirir; ayrıca LLM yolunda modelin başlık ya da spot döndürmediği
durumlarda kaynak metne düşen alanları da kapsar.

```bash
docker compose --profile translate up -d      # 2,5 GB indirir, ~3 GB RAM
# .env:  TRANSLATION_ENABLED=true
curl http://localhost:8081/health              # model yüklenmesi birkaç dakika
```

**LLM özet katmanının yerine geçmez.** Özet yazmak editöryal bir iştir — neyin
önemli olduğuna, neyin dışarıda kalacağına karar vermek — ve Türkçe onun yan
ürünüdür. Bir çevirmen bunların hiçbirini yapamaz.

**Neden LibreTranslate değil.** Ölçtük: en→tr motoru Kasım 2021 tarihli ~65M
parametrelik bir Argos paketi ve indekste daha yenisi yok. "React 20 ships a new
compiler" → "Reak 20 **gemi** yeni bir derleyici", "fixes a bug" → "bir **boğa**
düzeltiyor", "was released" → "**serbest bırakıldı**". Aynı çıktı canlı bir
public instance'ta da doğrulandı. Teknoloji haberinde bunu yayınlamak İngilizce
bırakmaktan kötüdür: okur İngilizce bir cümlenin etrafından dolaşabilir,
kaynağın söylemediği bir şeyi söyleyen Türkçe cümlenin etrafından dolaşamaz.
NLLB-200 de eleniyor — her boyutu CC-BY-**NC**, ticari kullanıma kapalı.

Varsayılan model **Qwen3-4B-Instruct-2507** (Q4_K_M, 2,5 GB): metin-only,
"düşünme" modu yok — thinking sürümleri yanıta `<think>` blokları koyar ve
`TranslationGuard` bunları haklı olarak reddeder — system prompt'a uyuyor
(kimliklerin korunmasını sağlayan şey o prompt) ve Türkçe destekli 119 dilden
biri. RAM varsa `TRANSLATION_MODEL_REPO=Mungert/Hunyuan-MT-7B-GGUF:q4_k_m`
(4,7 GB) çeviri için özel eğitilmiş bir modele geçirir.

> Model seçimi ölçüme değil, doğrulanmış kısıtlara dayanıyor: bu makinede model
> çalıştırıp karşılaştırma yapılmadı. Kabul edilebilir olmasının sebebi
> `TranslationGuard` — kötü çıktı yayınlanmıyor, reddediliyor. Bir modelin bu
> iş için iyi olup olmadığını, açtıktan sonra günlüklerdeki reddedilme oranına
> bakarak görürsün.

Her aday çeviri `TranslationGuard`'dan geçer ve **kapalı devre başarısız olur**:
sürüm numarası, ürün adı ya da CVE kimliği kaybolmuşsa, uzunluk oranı çökmüş ya
da patlamışsa, veya yanıt başka bir alfabedeyse çeviri reddedilir ve kaynak metin
olduğu gibi yayınlanır. Reddedilmek bir hata değil, beklenen davranıştır.

---

## Geliştirme

```bash
# Backend
cd backend
dotnet build
dotnet test                      # 120 test
dotnet run --project src/FocusAI.Api

# Frontend
cd frontend
npm install
npm run dev                      # http://localhost:3000
npm run typecheck
```

Postgres'i tek başına çalıştırmak için:

```bash
docker run -d --name focusai-pg -p 5432:5432 \
  -e POSTGRES_USER=focusai -e POSTGRES_PASSWORD=focusai -e POSTGRES_DB=focusai \
  pgvector/pgvector:pg16
```

### Migration

```bash
cd backend
dotnet ef migrations add <Ad> \
  --project src/FocusAI.Infrastructure \
  --startup-project src/FocusAI.Api \
  --output-dir Persistence/Migrations
```

---

## Yapılandırma

| Anahtar | Varsayılan | Açıklama |
|---|---|---|
| `ConnectionStrings:Postgres` | localhost | **pgvector eklentisi zorunlu** |
| `ConnectionStrings:Redis` | _(boş)_ | Boşsa bellek içi önbellek |
| `Jwt:SigningKey` | _(boş)_ | En az 32 bayt; boşsa API açılmaz |
| `Llm:Provider` | `Disabled` | `OpenAI` \| `Anthropic` \| `Gemini` \| `OpenRouter` \| `Ollama` \| `VLlm` \| `LmStudio` |
| `Embeddings:Provider` | `Disabled` | Boşsa yerel hash tabanlı embedding |
| `Search:Url` | _(boş)_ | Boşsa veritabanı üzerinden token araması |
| `Ingestion:EnableBackgroundJobs` | `true` | Hangfire zamanlanmış işleri |
| `NEXT_PUBLIC_API_URL` | `http://localhost:5210` | **Build zamanında** gömülür |

---

## Bilinen sınırlar

Dürüst olmak gerekirse, bu MVP'de eksik bırakılan ve bilinçli olarak
işaretlenen noktalar:

- **Web Push gönderimi yok.** Abonelik saklama, sessiz saatler, önem eşiği ve
  zamanlanmış iş tamamlandı; `LoggingPushNotifier` gerçek gönderim yerine log
  yazar. VAPID imzalama + payload şifrelemesi bir bağımlılık ve anahtar yönetimi
  kararıdır, tahminle yazılmadı.
- **Reddit ve GitHub kaynakları kimlik doğrulaması ister.** İkisi de artık
  kimliksiz veri merkezi isteklerini 403 ile reddediyor; token verilmeden bu
  kaynaklar hata sayacına düşer (hat çalışmaya devam eder).
- **Yerel hash embedding semantik değildir.** Yalnızca sözcük örtüşmesini
  yakalar. `docker compose up`'ın anahtarsız çalışması içindir; semantik
  kalite için gerçek bir embedding sağlayıcısı tanımlayın.
- **Premium (§16), takım çalışma alanı ve API ürünü kapsam dışıdır** — PRD
  yol haritasında v2'de.
