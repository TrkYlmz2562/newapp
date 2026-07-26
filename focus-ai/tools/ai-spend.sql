-- Focus AI — how much has the AI actually cost?
--
--   docker compose exec -T postgres psql -U focusai -d focusai -f - < tools/ai-spend.sql
--
-- Reads the app's own per-call ledger (ai_usage_logs), which records the token
-- counts the provider reported. This is the number to trust when reconciling a
-- Google bill: it is measured locally, per call, and it is broken down by
-- operation so an unexpected total can be traced to the feature that caused it.
--
-- Prices are declared once below, in USD per 1M tokens. Check them against
-- https://ai.google.dev/pricing before reading the cost column as gospel — the
-- token counts are facts, the money is arithmetic on top of a rate you supply.

-- NOTE: psql's \set takes the whole rest of the line as the value, so these
-- cannot carry trailing comments.
--
-- USD per 1M tokens. Defaults are Gemini 2.5 Flash-Lite. Check section 4's model
-- column before trusting the total — the rates differ by 4x across the family:
--   gemini-2.5-flash-lite   0.10 / 0.40
--   gemini-3.5-flash-lite   0.30 / 2.50
--   gemini-2.5-flash        0.30 / 2.50
-- usd_try: the rate on the day you were charged. Google fixes the TRY rate
-- monthly, so a month-old charge used a different one than today's.
\set in_price 0.10
\set out_price 0.40
\set usd_try 47.3

\echo ''
\echo '=== 1. Toplam (bugüne kadar) ==============================================='

SELECT
    count(*)                                    AS cagri,
    count(*) FILTER (WHERE NOT "Succeeded")     AS basarisiz,
    -- Only successful calls are billed: a 429 or a 404 costs nothing, and
    -- counting them would inflate the estimate exactly when something is wrong.
    sum("PromptTokens")     FILTER (WHERE "Succeeded")   AS girdi_token,
    sum("CompletionTokens") FILTER (WHERE "Succeeded")   AS cikti_token,
    round((sum("PromptTokens")  FILTER (WHERE "Succeeded") / 1e6 * :in_price
         + sum("CompletionTokens") FILTER (WHERE "Succeeded") / 1e6 * :out_price)::numeric, 4) AS usd,
    round((sum("PromptTokens")  FILTER (WHERE "Succeeded") / 1e6 * :in_price
         + sum("CompletionTokens") FILTER (WHERE "Succeeded") / 1e6 * :out_price)::numeric
         * :usd_try, 2)                                                          AS tl
FROM ai_usage_logs;

\echo ''
\echo '=== 2. Hangi özellik ne harcadı? ==========================================='

SELECT
    "Operation"                                 AS islem,
    count(*)                                    AS cagri,
    sum("PromptTokens")     FILTER (WHERE "Succeeded")   AS girdi_token,
    sum("CompletionTokens") FILTER (WHERE "Succeeded")   AS cikti_token,
    round((sum("PromptTokens")  FILTER (WHERE "Succeeded") / 1e6 * :in_price
         + sum("CompletionTokens") FILTER (WHERE "Succeeded") / 1e6 * :out_price)::numeric
         * :usd_try, 2)                                                          AS tl
FROM ai_usage_logs
GROUP BY "Operation"
ORDER BY 5 DESC NULLS LAST;

\echo ''
\echo '=== 3. Günlük seyir (son 30 gün) ==========================================='

SELECT
    date_trunc('day', "OccurredAt")::date       AS gun,
    count(*)                                    AS cagri,
    round((sum("PromptTokens")  FILTER (WHERE "Succeeded") / 1e6 * :in_price
         + sum("CompletionTokens") FILTER (WHERE "Succeeded") / 1e6 * :out_price)::numeric
         * :usd_try, 2)                                                          AS tl
FROM ai_usage_logs
WHERE "OccurredAt" > now() - interval '30 days'
GROUP BY 1
ORDER BY 1 DESC;

\echo ''
\echo '=== 4. Model bazında (fiyatlar modele göre değişir!) ======================='

SELECT
    "Model"                                     AS model,
    count(*)                                    AS cagri,
    sum("PromptTokens")                         AS girdi_token,
    sum("CompletionTokens")                     AS cikti_token
FROM ai_usage_logs
GROUP BY "Model"
ORDER BY 2 DESC;

\echo ''
\echo '=== 5. Başarısız çağrılar (para harcamaz ama sorunu gösterir) ==============='

SELECT
    "Model"                                     AS model,
    "Operation"                                 AS islem,
    count(*)                                    AS adet,
    max("OccurredAt")                           AS son,
    left(max("Error"), 120)                     AS ornek_hata
FROM ai_usage_logs
WHERE NOT "Succeeded"
GROUP BY 1, 2
ORDER BY 3 DESC
LIMIT 10;
