// 极简 WebView 壳：手机遥控页面的 Android 封装
plugins {
    id("com.android.application") version "8.5.2"
}

android {
    namespace = "net.rxnel.companion"
    compileSdk = 34

    defaultConfig {
        applicationId = "net.rxnel.companion"
        minSdk = 24
        targetSdk = 34
        versionCode = 1
        versionName = "1.0.0"
    }

    buildTypes {
        release {
            isMinifyEnabled = false
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

dependencies {
    implementation("androidx.appcompat:appcompat:1.7.0")
}
