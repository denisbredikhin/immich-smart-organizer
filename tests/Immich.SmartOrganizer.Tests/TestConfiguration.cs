namespace Immich.SmartOrganizer.Tests;

internal static class TestConfiguration
{
    public const string ScanYaml = """
        immich:
          url: "http://localhost:2283"
          apiKeyEnv: IMMICH_API_KEY
        scanner:
          batchSize: 10
        processing:
          dryRun: true
          maxAssetsPerRun: 20
        state:
          path: ./state.db
        categories:
          - name: receipts
            enabled: true
            search:
              contextual:
                - receipt
              ocr:
                - invoice
            match:
              any:
                - ocrAny:
                    - invoice
            actions:
              archive: false
        """;

    public const string ValidYaml = """
        immich:
          url: "http://localhost:2283"
          apiKeyEnv: IMMICH_API_KEY
        scanner:
          mode: incremental
          batchSize: 100
          concurrency: 2
          includeArchived: true
          includeExternalLibraries: true
          includeVideos: false
          lookbackDays: 7
        processing:
          dryRun: true
          maxAssetsPerRun: 1000
          maxArchiveActionsPerRun: 25
          maxErrorsPerRun: 20
          ambiguousAssetPolicy: review
        state:
          path: ./state.db
        categories:
          - name: receipts
            enabled: true
            search:
              contextual:
                - receipt
                - restaurant bill
              ocr:
                - kassabon
                - receipt
                - btw
            match:
              all:
                - contextualAny:
                    - receipt
                    - restaurant bill
                - ocrAny:
                    - kassabon
                    - receipt
                    - btw
            actions:
              album: Receipts
              tag: Documents/Receipts
              archive: false
          - name: invoices
            enabled: true
            search:
              contextual:
                - invoice
                - printed invoice
              ocr:
                - factuur
                - invoice
                - invoice number
            match:
              any:
                - ocrAny:
                    - factuur
                    - invoice
            actions:
              album: Invoices
              archive: false
        logging:
          level: Information
          format: text
        """;
}
