import Flutter
import UIKit

@main
@objc class AppDelegate: FlutterAppDelegate, FlutterImplicitEngineDelegate {
  private var sharing: FlutterMethodChannel?
  private var appInfo: FlutterMethodChannel?
  override func application(_ application: UIApplication, didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?) -> Bool {
    if let directory = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first {
      try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
      var protected = directory
      var values = URLResourceValues()
      values.isExcludedFromBackup = true
      try? protected.setResourceValues(values)
    }
    return super.application(application, didFinishLaunchingWithOptions: launchOptions)
  }
  func didInitializeImplicitFlutterEngine(_ engineBridge: FlutterImplicitEngineBridge) {
    GeneratedPluginRegistrant.register(with: engineBridge.pluginRegistry)
    guard let registrar = engineBridge.pluginRegistry.registrar(forPlugin: "TMusicSharedLinks") else { return }
    sharing = FlutterMethodChannel(name: "tmusic/shared_links", binaryMessenger: registrar.messenger())
    sharing?.setMethodCallHandler { call, result in
      guard call.method == "takePending" else { result(FlutterMethodNotImplemented); return }
      guard let shared = UserDefaults(suiteName: "group.com.tmusic.tmusic") else { result([]); return }
      let values = shared.stringArray(forKey: "pendingLinks") ?? []
      shared.removeObject(forKey: "pendingLinks")
      result(values)
    }
    appInfo = FlutterMethodChannel(name: "tmusic/app_info", binaryMessenger: registrar.messenger())
    appInfo?.setMethodCallHandler { call, result in
      guard call.method == "getAppInfo" else { result(FlutterMethodNotImplemented); return }
      let info = Bundle.main.infoDictionary
      let version = info?["CFBundleShortVersionString"] as? String ?? ""
      let buildNumber = info?["CFBundleVersion"] as? String ?? ""
      result(["version": version, "buildNumber": buildNumber])
    }
  }
}
