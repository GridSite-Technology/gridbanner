// Simple script to generate Teams app icons
// Requires: npm install canvas (or use online tool)

const fs = require('fs');
const path = require('path');

// For now, create a simple HTML file that can be used to generate icons
// User can screenshot this or use an online tool

const iconHtml = `<!DOCTYPE html>
<html>
<head>
    <style>
        body {
            margin: 0;
            padding: 0;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            background: #f0f0f0;
        }
        .icon-container {
            display: flex;
            gap: 50px;
        }
        .icon {
            background: #00ADEE;
            color: white;
            display: flex;
            align-items: center;
            justify-content: center;
            font-family: Arial, sans-serif;
            font-weight: bold;
            box-shadow: 0 4px 8px rgba(0,0,0,0.2);
        }
        .icon-color {
            width: 192px;
            height: 192px;
            font-size: 48px;
            border-radius: 20px;
        }
        .icon-outline {
            width: 32px;
            height: 32px;
            font-size: 8px;
            border-radius: 4px;
            border: 2px solid #00ADEE;
            background: white;
            color: #00ADEE;
        }
    </style>
</head>
<body>
    <div class="icon-container">
        <div class="icon icon-color">GB</div>
        <div class="icon icon-outline">GB</div>
    </div>
    <script>
        // Instructions
        console.log('To generate icons:');
        console.log('1. Take a screenshot of the large icon (192x192)');
        console.log('2. Crop to exactly 192x192 pixels');
        console.log('3. Save as icon-color.png');
        console.log('4. Take a screenshot of the small icon (32x32)');
        console.log('5. Crop to exactly 32x32 pixels');
        console.log('6. Save as icon-outline.png');
    </script>
</body>
</html>`;

fs.writeFileSync(path.join(__dirname, 'generate-icons.html'), iconHtml);
console.log('Created generate-icons.html - open in browser and screenshot to create icons');

