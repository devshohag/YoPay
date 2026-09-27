plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.yopay.device"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.yopay.device"
        minSdk = 26          // notification listener and the Keystore EC support below
        targetSdk = 35
        versionCode = 1
        versionName = "1.0"
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }
}

// Two dependencies, both from the platform's own support libraries.
//
// No Room, no Retrofit, no OkHttp, no Compose. Every one of those is a code generator or a
// version that has to line up with another version, and this app has to build on a laptop
// six months from now without an afternoon of dependency archaeology. The queue is three
// columns of SQLite, the HTTP is HttpURLConnection, the JSON is org.json - all of it ships
// with Android.
dependencies {
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.appcompat:appcompat:1.7.0")
}
