import UIKit
import Photos
import SwiftUI

class WallpaperHelper {
    static let shared = WallpaperHelper()

    // MARK: - Save to Photos Library
    func saveToPhotoLibrary(_ image: UIImage) async throws {
        let status = await PHPhotoLibrary.requestAuthorization(for: .addOnly)
        guard status == .authorized || status == .limited else {
            throw WallpaperError.photoAccessDenied
        }

        return try await withCheckedThrowingContinuation { continuation in
            PHPhotoLibrary.shared().performChanges {
                PHAssetChangeRequest.creationRequestForAsset(from: image)
            } completionHandler: { success, error in
                if let error {
                    continuation.resume(throwing: error)
                } else if success {
                    continuation.resume()
                } else {
                    continuation.resume(throwing: WallpaperError.saveFailed)
                }
            }
        }
    }

    // MARK: - Save all fetched photos
    func saveAllPhotos(_ photos: [PhotoItem]) async -> (saved: Int, failed: Int) {
        var saved = 0
        var failed = 0

        for photo in photos {
            do {
                let data = try await InstagramService.shared.loadImageData(from: photo.imageURL)
                if let image = UIImage(data: data) {
                    try await saveToPhotoLibrary(image)
                    saved += 1
                } else {
                    failed += 1
                }
            } catch {
                failed += 1
            }
        }

        return (saved, failed)
    }

    // MARK: - Open wallpaper settings via Shortcuts deeplink
    func openWallpaperSettings() {
        // iOS Ayarlar uygulaması üzerinden (en güvenilir yol)
        if let url = URL(string: "App-prefs:Wallpaper") {
            UIApplication.shared.open(url)
        }
    }

    // MARK: - Share image (for manual wallpaper setting)
    func shareImage(_ image: UIImage, from viewController: UIViewController) {
        let activityVC = UIActivityViewController(
            activityItems: [image],
            applicationActivities: nil
        )
        viewController.present(activityVC, animated: true)
    }
}

enum WallpaperError: LocalizedError {
    case photoAccessDenied
    case saveFailed
    case noImage

    var errorDescription: String? {
        switch self {
        case .photoAccessDenied: return "Fotoğraf izni verilmedi. Ayarlar'dan izin verin."
        case .saveFailed: return "Fotoğraf kaydedilemedi."
        case .noImage: return "Fotoğraf yüklenemedi."
        }
    }
}

// MARK: - SwiftUI UIViewController wrapper for sharing
struct ShareSheet: UIViewControllerRepresentable {
    let items: [Any]

    func makeUIViewController(context: Context) -> UIActivityViewController {
        UIActivityViewController(activityItems: items, applicationActivities: nil)
    }

    func updateUIViewController(_ uiViewController: UIActivityViewController, context: Context) {}
}
