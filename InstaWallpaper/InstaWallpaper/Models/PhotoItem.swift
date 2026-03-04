import Foundation

struct PhotoItem: Identifiable, Codable, Equatable {
    let id: String
    let imageURL: String
    let thumbnailURL: String
    let caption: String?
    let timestamp: Date?
    let postURL: String?

    static func == (lhs: PhotoItem, rhs: PhotoItem) -> Bool {
        lhs.id == rhs.id
    }
}
