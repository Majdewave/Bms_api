# Imaging media export deployment

The export endpoint is `GET /api/imaging/studies/{studyId}/export-media`. It requires the existing `view_clients` policy and reads the study's original DICOM objects from the configured private imaging bucket.

## Pinned Weasis asset

The Weasis 4.8.0 Windows x86-64 application and its `LICENSES` directory are supplied as a separate deployment asset, not committed to this repository or downloaded during requests. The vetted upload archive is named `Clienta-Weasis-Portable-4.8.0.zip` and contains only `VIEWER/` and `LICENSES/`.

Pinned archive SHA-256:

`532DA2D1D799100B3B5AA6C2E6EA2CD5D08F2A6E1FFDE5D86120E94C8C44D7CE`

Upload that exact archive to a private, versioned S3 asset bucket at:

`weasis/Weasis-4.8.0.zip`

Configure the API environment with:

- `AWS__WeasisAssetsBucketName`: private bucket holding the asset.
- `AWS__WeasisPortablePackageKey`: optional; defaults to `weasis/Weasis-4.8.0.zip`.

The API instance role needs `s3:GetObject` permission for that object. Keep public access blocked. The application verifies the pinned SHA-256 and caches the verified archive in its local temporary directory, so requests do not repeatedly fetch the viewer. Do not replace the object with a different build; a different SHA-256 is rejected.

## Runtime storage

Media exports use temporary disk files and permit one active export per API process to bound concurrent temporary-storage use. Capacity planning should allow room for the completed ZIP and one DICOM instance being staged. Temporary DICOM files are removed after the archive is built, and the response archive is deleted when its stream is closed. No database migration or imaging-pipeline change is required.

The export route's nginx proxy read timeout is 30 minutes. If the deployed Elastic Beanstalk environment uses a load balancer with a shorter idle timeout, its idle timeout must also exceed the expected archive-preparation time.

The generated `START_VIEWER.bat` resolves the viewer and DICOM paths relative to itself. It has no Clienta endpoint or credentials and does not use Windows AutoRun.
