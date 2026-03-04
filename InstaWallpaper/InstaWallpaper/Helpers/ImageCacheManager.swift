import UIKit
import SwiftUI

actor ImageCacheManager {
    static let shared = ImageCacheManager()

    private var cache: [String: UIImage] = [:]
    private var downloadTasks: [String: Task<UIImage, Error>] = [:]
    private let maxCacheSize = 30

    func image(for urlString: String) async throws -> UIImage {
        if let cached = cache[urlString] {
            return cached
        }

        if let existing = downloadTasks[urlString] {
            return try await existing.value
        }

        let task = Task<UIImage, Error> {
            let data = try await InstagramService.shared.loadImageData(from: urlString)
            guard let image = UIImage(data: data) else {
                throw URLError(.cannotDecodeContentData)
            }
            return image
        }

        downloadTasks[urlString] = task

        do {
            let image = try await task.value
            downloadTasks.removeValue(forKey: urlString)
            storeInCache(image, for: urlString)
            return image
        } catch {
            downloadTasks.removeValue(forKey: urlString)
            throw error
        }
    }

    private func storeInCache(_ image: UIImage, for key: String) {
        if cache.count >= maxCacheSize {
            cache.removeValue(forKey: cache.keys.first!)
        }
        cache[key] = image
    }

    func preloadImages(urls: [String]) {
        for url in urls.prefix(5) {
            Task {
                _ = try? await image(for: url)
            }
        }
    }

    func clearCache() {
        cache.removeAll()
    }
}

// MARK: - AsyncImage with cache
struct CachedAsyncImage: View {
    let urlString: String
    let contentMode: ContentMode

    @State private var uiImage: UIImage?
    @State private var isLoading = true
    @State private var hasFailed = false

    var body: some View {
        Group {
            if let uiImage {
                Image(uiImage: uiImage)
                    .resizable()
                    .aspectRatio(contentMode: contentMode)
                    .transition(.opacity.animation(.easeIn(duration: 0.3)))
            } else if isLoading {
                ZStack {
                    Color.black
                    ProgressView()
                        .tint(.white)
                        .scaleEffect(1.5)
                }
            } else {
                ZStack {
                    Color(white: 0.1)
                    VStack(spacing: 12) {
                        Image(systemName: "photo.badge.exclamationmark")
                            .font(.system(size: 40))
                            .foregroundColor(.gray)
                        Text("Yüklenemedi")
                            .foregroundColor(.gray)
                            .font(.caption)
                    }
                }
            }
        }
        .task(id: urlString) {
            await loadImage()
        }
    }

    private func loadImage() async {
        isLoading = true
        hasFailed = false
        do {
            let img = try await ImageCacheManager.shared.image(for: urlString)
            withAnimation {
                uiImage = img
                isLoading = false
            }
        } catch {
            withAnimation {
                isLoading = false
                hasFailed = true
            }
        }
    }
}
