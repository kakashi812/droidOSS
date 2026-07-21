// Must be a real import: inside the Kotlin DSL, a bare `java.util.Properties`
// resolves `java` to Gradle's own `java` extension rather than the package.
import java.util.Properties

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
}

android {
    namespace = "io.github.kakashi812.droidoss"
    compileSdk {
        // 37, not 36.1: androidx.core[-ktx] 1.19.0 refuses to compile against
        // anything older. compileSdk only controls which APIs are visible at
        // compile time -- targetSdk (36) and minSdk (26) are what affect
        // runtime behaviour and device support, and both stay put.
        version = release(37)
    }

    defaultConfig {
        applicationId = "io.github.kakashi812.droidoss"
        minSdk = 26
        targetSdk = 36
        versionCode = 1
        versionName = "0.1.0"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    // Read from local.properties, which is gitignored. The keystore and its
    // passwords must never reach the repository: anyone holding them can ship
    // an update that installs over a real droidOSS install.
    //
    // Losing the keystore is equally unrecoverable -- Android identifies an app
    // by its signature, so a replacement key means existing users must
    // uninstall before they can update. Back it up somewhere that is not this
    // machine.
    val keystoreProperties = Properties().apply {
        val file = rootProject.file("local.properties")
        if (file.exists()) file.inputStream().use { load(it) }
    }
    val keystorePath = keystoreProperties.getProperty("storeFile")

    signingConfigs {
        if (keystorePath != null) {
            create("release") {
                storeFile = file(keystorePath)
                storePassword = keystoreProperties.getProperty("storePassword")
                keyAlias = keystoreProperties.getProperty("keyAlias")
                keyPassword = keystoreProperties.getProperty("keyPassword")
            }
        }
    }

    buildTypes {
        release {
            optimization {
                enable = false
            }
            // Falls back to an unsigned release build when no keystore is
            // configured, so a fresh clone still builds.
            signingConfig = signingConfigs.findByName("release")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_11
        targetCompatibility = JavaVersion.VERSION_11
    }
    buildFeatures {
        compose = true
    }
}

dependencies {
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.compose.material3)
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.graphics)
    implementation(libs.androidx.compose.ui.tooling.preview)
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.lifecycle.runtime.ktx)
    testImplementation(libs.junit)
    androidTestImplementation(platform(libs.androidx.compose.bom))
    androidTestImplementation(libs.androidx.compose.ui.test.junit4)
    androidTestImplementation(libs.androidx.espresso.core)
    androidTestImplementation(libs.androidx.junit)
    debugImplementation(libs.androidx.compose.ui.test.manifest)
    debugImplementation(libs.androidx.compose.ui.tooling)
}