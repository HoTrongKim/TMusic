import UIKit
import Social
import UniformTypeIdentifiers

final class ShareViewController: SLComposeServiceViewController {
  override func isContentValid() -> Bool { true }
  override func didSelectPost() {
    let group = DispatchGroup()
    let lock = NSLock()
    var collected: [String] = []
    let attachments = (extensionContext?.inputItems as? [NSExtensionItem] ?? []).flatMap { $0.attachments ?? [] }.prefix(8)
    for item in attachments {
      let type = item.hasItemConformingToTypeIdentifier(UTType.url.identifier) ? UTType.url.identifier : UTType.plainText.identifier
      guard item.hasItemConformingToTypeIdentifier(type) else { continue }
      group.enter()
      item.loadItem(forTypeIdentifier: type, options: nil) { value, _ in
        defer { group.leave() }
        let text = (value as? URL)?.absoluteString ?? value as? String
        if let text = text, text.count <= 16384 {
          lock.lock(); collected.append(text); lock.unlock()
        }
      }
    }
    group.notify(queue: .main) {
      let shared = UserDefaults(suiteName: "group.com.tmusic.tmusic")
      let existing = shared?.stringArray(forKey: "pendingLinks") ?? []
      let merged = Array(Set(existing + collected)).prefix(8)
      shared?.set(Array(merged), forKey: "pendingLinks")
      self.extensionContext?.completeRequest(returningItems: [], completionHandler: nil)
    }
  }
  override func configurationItems() -> [Any]! { [] }
}
