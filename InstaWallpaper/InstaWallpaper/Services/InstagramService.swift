import Foundation

enum InstagramError: LocalizedError {
    case invalidUsername
    case networkError(Error)
    case parsingError
    case noPhotosFound
    case rateLimited

    var errorDescription: String? {
        switch self {
        case .invalidUsername: return "Geçersiz kullanıcı adı"
        case .networkError(let e): return "Ağ hatası: \(e.localizedDescription)"
        case .parsingError: return "Fotoğraflar alınamadı - Instagram yapısı değişmiş olabilir"
        case .noPhotosFound: return "Bu hesapta fotoğraf bulunamadı veya hesap gizli"
        case .rateLimited: return "Çok fazla istek. Lütfen birkaç dakika bekleyin."
        }
    }
}

actor InstagramService {
    static let shared = InstagramService()

    private let session: URLSession = {
        let config = URLSessionConfiguration.default
        config.timeoutIntervalForRequest = 15
        config.timeoutIntervalForResource = 30
        config.httpAdditionalHeaders = [
            "User-Agent": "Mozilla/5.0 (iPhone; CPU iPhone OS 16_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.0 Mobile/15E148 Safari/604.1",
            "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
            "Accept-Language": "tr-TR,tr;q=0.9,en-US;q=0.8",
            "Accept-Encoding": "gzip, deflate, br"
        ]
        return URLSession(configuration: config)
    }()

    // MARK: - Main fetch entry point
    func fetchPhotos(username: String) async throws -> [PhotoItem] {
        let trimmed = username.trimmingCharacters(in: .whitespacesAndNewlines)
            .replacingOccurrences(of: "@", with: "")
        guard !trimmed.isEmpty else { throw InstagramError.invalidUsername }

        // Try multiple strategies in order
        if let photos = try? await fetchViaProfilePage(username: trimmed), !photos.isEmpty {
            return photos
        }
        if let photos = try? await fetchViaPicuki(username: trimmed), !photos.isEmpty {
            return photos
        }
        throw InstagramError.noPhotosFound
    }

    // MARK: - Strategy 1: Instagram profile page scraping
    private func fetchViaProfilePage(username: String) async throws -> [PhotoItem] {
        guard let url = URL(string: "https://www.instagram.com/\(username)/") else {
            throw InstagramError.invalidUsername
        }

        var request = URLRequest(url: url)
        request.setValue("https://www.instagram.com/", forHTTPHeaderField: "Referer")

        let (data, response) = try await session.data(for: request)
        guard let httpResponse = response as? HTTPURLResponse else {
            throw InstagramError.networkError(URLError(.badServerResponse))
        }
        if httpResponse.statusCode == 429 { throw InstagramError.rateLimited }
        guard httpResponse.statusCode == 200 else { throw InstagramError.parsingError }

        guard let html = String(data: data, encoding: .utf8) else {
            throw InstagramError.parsingError
        }

        return try parsePhotosFromHTML(html: html, username: username)
    }

    private func parsePhotosFromHTML(html: String, username: String) throws -> [PhotoItem] {
        var photos: [PhotoItem] = []

        // Extract shared data JSON from script tags
        let patterns = [
            #"window\._sharedData\s*=\s*(\{.*?\});</script>"#,
            #"window\.__additionalDataLoaded\s*\([^,]+,\s*(\{.*?\})\);"#
        ]

        for pattern in patterns {
            if let photos = try? extractPhotosFromPattern(html: html, pattern: pattern, username: username), !photos.isEmpty {
                return photos
            }
        }

        // Fallback: extract image URLs directly from og:image and similar meta tags
        photos = extractPhotosFromMetaTags(html: html, username: username)
        return photos
    }

    private func extractPhotosFromPattern(html: String, pattern: String, username: String) throws -> [PhotoItem] {
        guard let regex = try? NSRegularExpression(pattern: pattern, options: [.dotMatchesLineSeparators]),
              let match = regex.firstMatch(in: html, range: NSRange(html.startIndex..., in: html)),
              let range = Range(match.range(at: 1), in: html) else {
            return []
        }

        let jsonString = String(html[range])
        guard let jsonData = jsonString.data(using: .utf8),
              let json = try? JSONSerialization.jsonObject(with: jsonData) as? [String: Any] else {
            return []
        }

        return extractPhotosFromJSON(json: json, username: username)
    }

    private func extractPhotosFromJSON(json: [String: Any], username: String) -> [PhotoItem] {
        var items: [PhotoItem] = []

        func traverse(_ obj: Any, depth: Int = 0) {
            guard depth < 10 else { return }
            if let dict = obj as? [String: Any] {
                // Look for media nodes
                if let displayURL = dict["display_url"] as? String,
                   let shortcode = dict["shortcode"] as? String {
                    let id = dict["id"] as? String ?? shortcode
                    let caption = ((dict["edge_media_to_caption"] as? [String: Any])?["edges"] as? [[String: Any]])?.first?["node"] as? [String: Any]?["text"] as? String
                    let thumbnailURL = (dict["thumbnail_src"] as? String) ?? displayURL

                    let item = PhotoItem(
                        id: id,
                        imageURL: displayURL,
                        thumbnailURL: thumbnailURL,
                        caption: caption,
                        timestamp: nil,
                        postURL: "https://www.instagram.com/p/\(shortcode)/"
                    )
                    items.append(item)
                }
                for value in dict.values {
                    traverse(value, depth: depth + 1)
                }
            } else if let array = obj as? [Any] {
                for element in array {
                    traverse(element, depth: depth + 1)
                }
            }
        }

        traverse(json)
        // Deduplicate
        var seen = Set<String>()
        return items.filter { seen.insert($0.id).inserted }
    }

    private func extractPhotosFromMetaTags(html: String, username: String) -> [PhotoItem] {
        var items: [PhotoItem] = []
        let ogPattern = #"<meta[^>]+property="og:image"[^>]+content="([^"]+)""#
        guard let regex = try? NSRegularExpression(pattern: ogPattern) else { return [] }
        let matches = regex.matches(in: html, range: NSRange(html.startIndex..., in: html))
        for (i, match) in matches.enumerated() {
            if let range = Range(match.range(at: 1), in: html) {
                let urlStr = String(html[range])
                items.append(PhotoItem(
                    id: "meta_\(i)",
                    imageURL: urlStr,
                    thumbnailURL: urlStr,
                    caption: nil,
                    timestamp: nil,
                    postURL: nil
                ))
            }
        }
        return items
    }

    // MARK: - Strategy 2: Picuki scraping
    private func fetchViaPicuki(username: String) async throws -> [PhotoItem] {
        guard let url = URL(string: "https://www.picuki.com/profile/\(username)") else {
            throw InstagramError.invalidUsername
        }

        var request = URLRequest(url: url)
        request.setValue("https://www.picuki.com/", forHTTPHeaderField: "Referer")

        let (data, response) = try await session.data(for: request)
        guard let httpResponse = response as? HTTPURLResponse, httpResponse.statusCode == 200 else {
            throw InstagramError.parsingError
        }
        guard let html = String(data: data, encoding: .utf8) else {
            throw InstagramError.parsingError
        }

        return parsePicukiHTML(html: html)
    }

    private func parsePicukiHTML(html: String) -> [PhotoItem] {
        var items: [PhotoItem] = []
        // Picuki stores images in <img> tags with class "post-image"
        let pattern = #"<img[^>]+class="[^"]*post-image[^"]*"[^>]+src="([^"]+)""#
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return [] }
        let matches = regex.matches(in: html, range: NSRange(html.startIndex..., in: html))

        for (i, match) in matches.enumerated() {
            if let range = Range(match.range(at: 1), in: html) {
                let urlStr = String(html[range])
                // Filter out tiny thumbnails
                if urlStr.contains("http") && !urlStr.contains("s150x150") {
                    items.append(PhotoItem(
                        id: "picuki_\(i)",
                        imageURL: urlStr,
                        thumbnailURL: urlStr,
                        caption: nil,
                        timestamp: nil,
                        postURL: nil
                    ))
                }
            }
        }
        return items
    }

    // MARK: - Load image data
    func loadImageData(from urlString: String) async throws -> Data {
        guard let url = URL(string: urlString) else {
            throw URLError(.badURL)
        }
        var request = URLRequest(url: url)
        request.setValue("https://www.instagram.com/", forHTTPHeaderField: "Referer")
        let (data, _) = try await session.data(for: request)
        return data
    }
}
