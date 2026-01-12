// Microsoft Teams SDK initialization
let teamsContext = null;

microsoftTeams.app.initialize().then(() => {
    microsoftTeams.app.getContext().then(context => {
        teamsContext = context;
        loadConfig();
    });
});

// Configuration
let config = {
    serverUrl: '',
    apiKey: ''
};

// API Helper
async function apiCall(endpoint, method = 'GET', body = null) {
    if (!config.serverUrl || !config.apiKey) {
        throw new Error('Server URL and API Key must be configured');
    }

    const url = `${config.serverUrl}${endpoint}`;
    const options = {
        method,
        headers: {
            'Content-Type': 'application/json',
            'X-API-Key': config.apiKey
        }
    };

    if (body) {
        options.body = JSON.stringify(body);
    }

    const response = await fetch(url, options);
    
    if (!response.ok) {
        const errorText = await response.text();
        throw new Error(`API Error: ${response.status} - ${errorText}`);
    }

    if (response.status === 204) {
        return null;
    }

    return await response.json();
}

// Configuration Management
function loadConfig() {
    const saved = localStorage.getItem('gridbanner-config');
    if (saved) {
        config = JSON.parse(saved);
        document.getElementById('serverUrl').value = config.serverUrl || '';
        document.getElementById('apiKey').value = config.apiKey || '';
        
        if (config.serverUrl && config.apiKey) {
            testConnection();
        }
    }
}

function saveConfig() {
    config.serverUrl = document.getElementById('serverUrl').value.trim();
    config.apiKey = document.getElementById('apiKey').value.trim();

    if (!config.serverUrl || !config.apiKey) {
        showMessage('Please enter both Server URL and API Key', 'error');
        return;
    }

    localStorage.setItem('gridbanner-config', JSON.stringify(config));
    showMessage('Configuration saved!', 'success');
    testConnection();
}

async function testConnection() {
    try {
        await apiCall('/api/health');
        updateConnectionStatus(true);
        document.getElementById('mainContent').style.display = 'block';
        loadAllData();
    } catch (error) {
        updateConnectionStatus(false);
        showMessage(`Connection failed: ${error.message}`, 'error');
    }
}

function updateConnectionStatus(connected) {
    const statusEl = document.getElementById('connectionStatus');
    if (connected) {
        statusEl.textContent = 'Connected';
        statusEl.className = 'status-badge status-connected';
    } else {
        statusEl.textContent = 'Not Connected';
        statusEl.className = 'status-badge status-disconnected';
    }
}

// Tab Management
function switchTab(tabName) {
    // Update tab buttons
    document.querySelectorAll('.tab').forEach(tab => {
        tab.classList.remove('active');
    });
    event.target.classList.add('active');

    // Update tab content
    document.querySelectorAll('.tab-content').forEach(content => {
        content.classList.remove('active');
    });
    document.getElementById(`${tabName}Tab`).classList.add('active');

    // Load data for the tab
    if (tabName === 'alerts') {
        loadAlerts();
    } else if (tabName === 'templates') {
        loadTemplates();
    } else if (tabName === 'sites') {
        loadSites();
    } else if (tabName === 'systems') {
        loadSystems();
    } else if (tabName === 'audio') {
        loadAudioFiles();
    } else if (tabName === 'settings') {
        loadSettings();
    }
}

// Alert Management
async function loadAlerts() {
    const container = document.getElementById('alertsList');
    container.innerHTML = '<div class="loading">Loading alerts...</div>';

    try {
        const alert = await apiCall('/api/alert');
        
        if (!alert || Object.keys(alert).length === 0) {
            container.innerHTML = '<div class="empty-state">No active alert</div>';
            return;
        }

        const level = alert.level || 'routine';
        const levelClass = level === 'super_critical' ? 'critical' : 
                          level === 'critical' ? 'critical' : 
                          level === 'urgent' ? 'urgent' : 'routine';

        container.innerHTML = `
            <div class="alert-item ${levelClass}">
                <div class="item-header">
                    <div class="item-title">${alert.summary || 'Alert'}</div>
                    <div class="item-actions">
                        <button class="btn btn-secondary" onclick="clearAlert()">Clear Alert</button>
                    </div>
                </div>
                <div class="item-details">
                    <div><strong>Level:</strong> ${level}</div>
                    <div><strong>Message:</strong> ${alert.message || 'N/A'}</div>
                    <div><strong>Background:</strong> ${alert.background_color || 'N/A'}</div>
                    <div><strong>Foreground:</strong> ${alert.foreground_color || 'N/A'}</div>
                    ${alert.site ? `<div><strong>Site:</strong> ${alert.site}</div>` : ''}
                    ${alert.alert_contact_name ? `<div><strong>Contact:</strong> ${alert.alert_contact_name}</div>` : ''}
                </div>
            </div>
        `;
    } catch (error) {
        container.innerHTML = `<div class="error">Error loading alerts: ${error.message}</div>`;
    }
}

async function clearAlert() {
    if (!confirm('Are you sure you want to clear the active alert?')) {
        return;
    }

    try {
        await apiCall('/api/alert', 'DELETE');
        showMessage('Alert cleared successfully', 'success');
        loadAlerts();
    } catch (error) {
        showMessage(`Error clearing alert: ${error.message}`, 'error');
    }
}

function showNewAlertModal() {
    const modal = createModal('Create New Alert', `
        <form id="alertForm" onsubmit="createAlert(event)">
            <div class="form-group">
                <label>Template</label>
                <select id="alertTemplate" onchange="loadTemplateForAlert()">
                    <option value="">Select a template...</option>
                </select>
            </div>
            <div class="form-group">
                <label>Level</label>
                <select id="alertLevel" required>
                    <option value="routine">Routine</option>
                    <option value="urgent">Urgent</option>
                    <option value="critical">Critical</option>
                    <option value="super_critical">Super Critical</option>
                </select>
            </div>
            <div class="form-group">
                <label>Summary</label>
                <input type="text" id="alertSummary" required>
            </div>
            <div class="form-group">
                <label>Message</label>
                <textarea id="alertMessage" rows="3" required></textarea>
            </div>
            <div class="form-row">
                <div class="form-group">
                    <label>Background Color</label>
                    <input type="color" id="alertBgColor" value="#FF0000">
                </div>
                <div class="form-group">
                    <label>Foreground Color</label>
                    <input type="color" id="alertFgColor" value="#FFFFFF">
                </div>
            </div>
            <div class="form-group">
                <label>Site (optional - leave empty for all sites)</label>
                <select id="alertSite">
                    <option value="">All Sites</option>
                </select>
            </div>
            <div class="form-group">
                <label>Contact Name</label>
                <input type="text" id="alertContactName">
            </div>
            <div class="form-group">
                <label>Contact Phone</label>
                <input type="text" id="alertContactPhone">
            </div>
            <div class="form-group">
                <label>Contact Email</label>
                <input type="email" id="alertContactEmail">
            </div>
            <div class="form-group">
                <label>Contact Teams</label>
                <input type="text" id="alertContactTeams" placeholder="Email or Teams deep link">
            </div>
            <div style="margin-top: 20px;">
                <button type="submit" class="btn btn-primary">Create Alert</button>
                <button type="button" class="btn btn-secondary" onclick="closeModal()">Cancel</button>
            </div>
        </form>
    `);
    
    loadTemplatesForSelect('alertTemplate');
    loadSitesForSelect('alertSite');
    loadSettingsForAlert();
}

async function createAlert(event) {
    event.preventDefault();
    
    const alert = {
        level: document.getElementById('alertLevel').value,
        summary: document.getElementById('alertSummary').value,
        message: document.getElementById('alertMessage').value,
        background_color: document.getElementById('alertBgColor').value,
        foreground_color: document.getElementById('alertFgColor').value,
        site: document.getElementById('alertSite').value || null,
        alert_contact_name: document.getElementById('alertContactName').value || null,
        alert_contact_phone: document.getElementById('alertContactPhone').value || null,
        alert_contact_email: document.getElementById('alertContactEmail').value || null,
        alert_contact_teams: document.getElementById('alertContactTeams').value || null
    };

    try {
        await apiCall('/api/alert', 'POST', alert);
        showMessage('Alert created successfully', 'success');
        closeModal();
        loadAlerts();
    } catch (error) {
        showMessage(`Error creating alert: ${error.message}`, 'error');
    }
}

async function loadTemplateForAlert() {
    const templateId = document.getElementById('alertTemplate').value;
    if (!templateId) return;

    try {
        const data = await apiCall('/api/data');
        const template = data.templates.find(t => t.id === templateId);
        if (template) {
            document.getElementById('alertLevel').value = template.level;
            document.getElementById('alertSummary').value = template.summary;
            document.getElementById('alertMessage').value = template.message;
            document.getElementById('alertBgColor').value = template.background_color;
            document.getElementById('alertFgColor').value = template.foreground_color;
            document.getElementById('alertSite').value = template.site || '';
            document.getElementById('alertContactName').value = template.alert_contact_name || '';
            document.getElementById('alertContactPhone').value = template.alert_contact_phone || '';
            document.getElementById('alertContactEmail').value = template.alert_contact_email || '';
            document.getElementById('alertContactTeams').value = template.alert_contact_teams || '';
        }
    } catch (error) {
        showMessage(`Error loading template: ${error.message}`, 'error');
    }
}

// Template Management
async function loadTemplates() {
    const container = document.getElementById('templatesList');
    container.innerHTML = '<div class="loading">Loading templates...</div>';

    try {
        const data = await apiCall('/api/data');
        const templates = data.templates || [];

        if (templates.length === 0) {
            container.innerHTML = '<div class="empty-state">No templates found</div>';
            return;
        }

        container.innerHTML = templates.map(template => `
            <div class="template-item">
                <div class="item-header">
                    <div class="item-title">${template.name}</div>
                    <div class="item-actions">
                        <button class="btn btn-primary" onclick="useTemplate('${template.id}')">Use</button>
                        <button class="btn btn-secondary" onclick="editTemplate('${template.id}')">Edit</button>
                        <button class="btn btn-danger" onclick="deleteTemplate('${template.id}')">Delete</button>
                    </div>
                </div>
                <div class="item-details">
                    <div><strong>Level:</strong> ${template.level}</div>
                    <div><strong>Summary:</strong> ${template.summary}</div>
                    <div><strong>Message:</strong> ${template.message}</div>
                    ${template.site ? `<div><strong>Site:</strong> ${template.site}</div>` : '<div><strong>Site:</strong> All Sites</div>'}
                </div>
            </div>
        `).join('');
    } catch (error) {
        container.innerHTML = `<div class="error">Error loading templates: ${error.message}</div>`;
    }
}

async function useTemplate(templateId) {
    try {
        const data = await apiCall('/api/data');
        const template = data.templates.find(t => t.id === templateId);
        if (!template) {
            throw new Error('Template not found');
        }

        const alert = {
            level: template.level,
            summary: template.summary,
            message: template.message,
            background_color: template.background_color,
            foreground_color: template.foreground_color,
            site: template.site,
            alert_contact_name: template.alert_contact_name || data.settings.default_contact_name,
            alert_contact_phone: template.alert_contact_phone || data.settings.default_contact_phone,
            alert_contact_email: template.alert_contact_email || data.settings.default_contact_email,
            alert_contact_teams: template.alert_contact_teams || data.settings.default_contact_teams
        };

        await apiCall('/api/alert', 'POST', alert);
        showMessage('Alert created from template', 'success');
        switchTab('alerts');
        loadAlerts();
    } catch (error) {
        showMessage(`Error using template: ${error.message}`, 'error');
    }
}

function showNewTemplateModal(templateId = null) {
    const isEdit = templateId !== null;
    const modal = createModal(isEdit ? 'Edit Template' : 'Create New Template', `
        <form id="templateForm" onsubmit="saveTemplate(event, ${isEdit ? `'${templateId}'` : 'null'})">
            <div class="form-group">
                <label>Name</label>
                <input type="text" id="templateName" required>
            </div>
            <div class="form-group">
                <label>Level</label>
                <select id="templateLevel" required>
                    <option value="routine">Routine</option>
                    <option value="urgent">Urgent</option>
                    <option value="critical">Critical</option>
                    <option value="super_critical">Super Critical</option>
                </select>
            </div>
            <div class="form-group">
                <label>Summary</label>
                <input type="text" id="templateSummary" required>
            </div>
            <div class="form-group">
                <label>Message</label>
                <textarea id="templateMessage" rows="3" required></textarea>
            </div>
            <div class="form-row">
                <div class="form-group">
                    <label>Background Color</label>
                    <input type="color" id="templateBgColor" value="#FF0000">
                </div>
                <div class="form-group">
                    <label>Foreground Color</label>
                    <input type="color" id="templateFgColor" value="#FFFFFF">
                </div>
            </div>
            <div class="form-group">
                <label>Site (optional - leave empty for all sites)</label>
                <select id="templateSite">
                    <option value="">All Sites</option>
                </select>
            </div>
            <div class="form-group">
                <label>Contact Name</label>
                <input type="text" id="templateContactName">
            </div>
            <div class="form-group">
                <label>Contact Phone</label>
                <input type="text" id="templateContactPhone">
            </div>
            <div class="form-group">
                <label>Contact Email</label>
                <input type="email" id="templateContactEmail">
            </div>
            <div class="form-group">
                <label>Contact Teams</label>
                <input type="text" id="templateContactTeams" placeholder="Email or Teams deep link">
            </div>
            <div style="margin-top: 20px;">
                <button type="submit" class="btn btn-primary">${isEdit ? 'Update' : 'Create'} Template</button>
                <button type="button" class="btn btn-secondary" onclick="closeModal()">Cancel</button>
            </div>
        </form>
    `);

    loadSitesForSelect('templateSite');
    
    if (isEdit) {
        loadTemplateForEdit(templateId);
    } else {
        loadSettingsForTemplate();
    }
}

async function loadTemplateForEdit(templateId) {
    try {
        const data = await apiCall('/api/data');
        const template = data.templates.find(t => t.id === templateId);
        if (template) {
            document.getElementById('templateName').value = template.name;
            document.getElementById('templateLevel').value = template.level;
            document.getElementById('templateSummary').value = template.summary;
            document.getElementById('templateMessage').value = template.message;
            document.getElementById('templateBgColor').value = template.background_color;
            document.getElementById('templateFgColor').value = template.foreground_color;
            document.getElementById('templateSite').value = template.site || '';
            document.getElementById('templateContactName').value = template.alert_contact_name || '';
            document.getElementById('templateContactPhone').value = template.alert_contact_phone || '';
            document.getElementById('templateContactEmail').value = template.alert_contact_email || '';
            document.getElementById('templateContactTeams').value = template.alert_contact_teams || '';
        }
    } catch (error) {
        showMessage(`Error loading template: ${error.message}`, 'error');
    }
}

async function saveTemplate(event, templateId) {
    event.preventDefault();
    
    const template = {
        name: document.getElementById('templateName').value,
        level: document.getElementById('templateLevel').value,
        summary: document.getElementById('templateSummary').value,
        message: document.getElementById('templateMessage').value,
        background_color: document.getElementById('templateBgColor').value,
        foreground_color: document.getElementById('templateFgColor').value,
        site: document.getElementById('templateSite').value || null,
        alert_contact_name: document.getElementById('templateContactName').value || null,
        alert_contact_phone: document.getElementById('templateContactPhone').value || null,
        alert_contact_email: document.getElementById('templateContactEmail').value || null,
        alert_contact_teams: document.getElementById('templateContactTeams').value || null
    };

    try {
        if (templateId) {
            await apiCall(`/api/templates/${templateId}`, 'PUT', template);
            showMessage('Template updated successfully', 'success');
        } else {
            await apiCall('/api/templates', 'POST', template);
            showMessage('Template created successfully', 'success');
        }
        closeModal();
        loadTemplates();
    } catch (error) {
        showMessage(`Error saving template: ${error.message}`, 'error');
    }
}

async function editTemplate(templateId) {
    showNewTemplateModal(templateId);
}

async function deleteTemplate(templateId) {
    if (!confirm('Are you sure you want to delete this template?')) {
        return;
    }

    try {
        await apiCall(`/api/templates/${templateId}`, 'DELETE');
        showMessage('Template deleted successfully', 'success');
        loadTemplates();
    } catch (error) {
        showMessage(`Error deleting template: ${error.message}`, 'error');
    }
}

// Site Management
async function loadSites() {
    const container = document.getElementById('sitesList');
    container.innerHTML = '<div class="loading">Loading sites...</div>';

    try {
        const data = await apiCall('/api/data');
        const sites = data.sites || [];

        if (sites.length === 0) {
            container.innerHTML = '<div class="empty-state">No sites found</div>';
            return;
        }

        container.innerHTML = sites.map(site => `
            <div class="site-item">
                <div class="item-header">
                    <div class="item-title">${site.name}</div>
                    <div class="item-actions">
                        <button class="btn btn-secondary" onclick="editSite('${site.id}')">Edit</button>
                        <button class="btn btn-danger" onclick="deleteSite('${site.id}')">Delete</button>
                    </div>
                </div>
            </div>
        `).join('');
    } catch (error) {
        container.innerHTML = `<div class="error">Error loading sites: ${error.message}</div>`;
    }
}

function showNewSiteModal(siteId = null) {
    const isEdit = siteId !== null;
    const modal = createModal(isEdit ? 'Edit Site' : 'Add New Site', `
        <form id="siteForm" onsubmit="saveSite(event, ${isEdit ? `'${siteId}'` : 'null'})">
            <div class="form-group">
                <label>Site Name</label>
                <input type="text" id="siteName" required>
            </div>
            <div style="margin-top: 20px;">
                <button type="submit" class="btn btn-primary">${isEdit ? 'Update' : 'Create'} Site</button>
                <button type="button" class="btn btn-secondary" onclick="closeModal()">Cancel</button>
            </div>
        </form>
    `);

    if (isEdit) {
        loadSiteForEdit(siteId);
    }
}

async function loadSiteForEdit(siteId) {
    try {
        const data = await apiCall('/api/data');
        const site = data.sites.find(s => s.id === siteId);
        if (site) {
            document.getElementById('siteName').value = site.name;
        }
    } catch (error) {
        showMessage(`Error loading site: ${error.message}`, 'error');
    }
}

async function saveSite(event, siteId) {
    event.preventDefault();
    
    const site = {
        name: document.getElementById('siteName').value
    };

    try {
        if (siteId) {
            await apiCall(`/api/sites/${siteId}`, 'PUT', site);
            showMessage('Site updated successfully', 'success');
        } else {
            await apiCall('/api/sites', 'POST', site);
            showMessage('Site created successfully', 'success');
        }
        closeModal();
        loadSites();
    } catch (error) {
        showMessage(`Error saving site: ${error.message}`, 'error');
    }
}

async function editSite(siteId) {
    showNewSiteModal(siteId);
}

async function deleteSite(siteId) {
    if (!confirm('Are you sure you want to delete this site?')) {
        return;
    }

    try {
        await apiCall(`/api/sites/${siteId}`, 'DELETE');
        showMessage('Site deleted successfully', 'success');
        loadSites();
    } catch (error) {
        showMessage(`Error deleting site: ${error.message}`, 'error');
    }
}

// System Management
async function loadSystems() {
    const container = document.getElementById('systemsList');
    container.innerHTML = '<div class="loading">Loading systems...</div>';

    try {
        const data = await apiCall('/api/data');
        const systems = Object.values(data.systems || {});

        if (systems.length === 0) {
            container.innerHTML = '<div class="empty-state">No systems found</div>';
            return;
        }

        container.innerHTML = systems.map(system => `
            <div class="system-item">
                <div class="item-header">
                    <div class="item-title">${system.workstation_name}</div>
                </div>
                <div class="item-details">
                    <div><strong>Username:</strong> ${system.username}</div>
                    <div><strong>Classification:</strong> ${system.classification}</div>
                    <div><strong>Location:</strong> ${system.location}</div>
                    <div><strong>Company:</strong> ${system.company}</div>
                    <div><strong>Compliance:</strong> ${system.compliance_status === 1 ? 'Compliant' : 'Non-Compliant'}</div>
                    <div><strong>Last Seen:</strong> ${new Date(system.last_seen).toLocaleString()}</div>
                </div>
            </div>
        `).join('');
    } catch (error) {
        container.innerHTML = `<div class="error">Error loading systems: ${error.message}</div>`;
    }
}

// Audio Management
async function loadAudioFiles() {
    const container = document.getElementById('audioList');
    container.innerHTML = '<div class="loading">Loading audio files...</div>';

    try {
        const files = await apiCall('/api/audio');
        
        if (files.length === 0) {
            container.innerHTML = '<div class="empty-state">No audio files found</div>';
            return;
        }

        container.innerHTML = files.map(file => `
            <div class="audio-item">
                <div class="audio-name">${file.name}</div>
                <div class="audio-actions">
                    <button class="btn btn-secondary" onclick="renameAudio('${file.name}')">Rename</button>
                    <button class="btn btn-danger" onclick="deleteAudio('${file.name}')">Delete</button>
                </div>
            </div>
        `).join('');
    } catch (error) {
        container.innerHTML = `<div class="error">Error loading audio files: ${error.message}</div>`;
    }
}

function showUploadAudioModal() {
    const modal = createModal('Upload Audio File', `
        <form id="audioForm" onsubmit="uploadAudio(event)">
            <div class="form-group">
                <label>File Name</label>
                <input type="text" id="audioFileName" required placeholder="e.g., alert-sound.mp3">
            </div>
            <div class="form-group">
                <label>Select File</label>
                <input type="file" id="audioFile" accept="audio/*" required>
            </div>
            <div style="margin-top: 20px;">
                <button type="submit" class="btn btn-primary">Upload</button>
                <button type="button" class="btn btn-secondary" onclick="closeModal()">Cancel</button>
            </div>
        </form>
    `);
}

async function uploadAudio(event) {
    event.preventDefault();
    
    const fileName = document.getElementById('audioFileName').value;
    const fileInput = document.getElementById('audioFile');
    
    if (!fileInput.files || fileInput.files.length === 0) {
        showMessage('Please select a file', 'error');
        return;
    }

    const formData = new FormData();
    formData.append('file', fileInput.files[0]);
    formData.append('name', fileName);

    try {
        const response = await fetch(`${config.serverUrl}/api/audio`, {
            method: 'POST',
            headers: {
                'X-API-Key': config.apiKey
            },
            body: formData
        });

        if (!response.ok) {
            throw new Error(`Upload failed: ${response.statusText}`);
        }

        showMessage('Audio file uploaded successfully', 'success');
        closeModal();
        loadAudioFiles();
    } catch (error) {
        showMessage(`Error uploading audio: ${error.message}`, 'error');
    }
}

async function renameAudio(oldName) {
    const newName = prompt('Enter new name:', oldName);
    if (!newName || newName === oldName) {
        return;
    }

    try {
        await apiCall(`/api/audio/${encodeURIComponent(oldName)}`, 'PUT', { name: newName });
        showMessage('Audio file renamed successfully', 'success');
        loadAudioFiles();
    } catch (error) {
        showMessage(`Error renaming audio: ${error.message}`, 'error');
    }
}

async function deleteAudio(fileName) {
    if (!confirm(`Are you sure you want to delete "${fileName}"?`)) {
        return;
    }

    try {
        await apiCall(`/api/audio/${encodeURIComponent(fileName)}`, 'DELETE');
        showMessage('Audio file deleted successfully', 'success');
        loadAudioFiles();
    } catch (error) {
        showMessage(`Error deleting audio: ${error.message}`, 'error');
    }
}

// Settings Management
async function loadSettings() {
    try {
        const data = await apiCall('/api/data');
        const settings = data.settings || {};
        
        document.getElementById('defaultContactName').value = settings.default_contact_name || '';
        document.getElementById('defaultContactPhone').value = settings.default_contact_phone || '';
        document.getElementById('defaultContactEmail').value = settings.default_contact_email || '';
        document.getElementById('defaultContactTeams').value = settings.default_contact_teams || '';
    } catch (error) {
        showMessage(`Error loading settings: ${error.message}`, 'error');
    }
}

async function saveSettings() {
    const settings = {
        default_contact_name: document.getElementById('defaultContactName').value,
        default_contact_phone: document.getElementById('defaultContactPhone').value,
        default_contact_email: document.getElementById('defaultContactEmail').value,
        default_contact_teams: document.getElementById('defaultContactTeams').value
    };

    try {
        await apiCall('/api/settings', 'PUT', settings);
        showMessage('Settings saved successfully', 'success');
    } catch (error) {
        showMessage(`Error saving settings: ${error.message}`, 'error');
    }
}

// Helper Functions
function loadAllData() {
    loadAlerts();
    loadTemplates();
    loadSites();
    loadSystems();
    loadAudioFiles();
    loadSettings();
}

async function loadTemplatesForSelect(selectId) {
    try {
        const data = await apiCall('/api/data');
        const select = document.getElementById(selectId);
        select.innerHTML = '<option value="">Select a template...</option>' +
            data.templates.map(t => `<option value="${t.id}">${t.name}</option>`).join('');
    } catch (error) {
        console.error('Error loading templates:', error);
    }
}

async function loadSitesForSelect(selectId) {
    try {
        const data = await apiCall('/api/data');
        const select = document.getElementById(selectId);
        const currentValue = select.value;
        select.innerHTML = '<option value="">All Sites</option>' +
            data.sites.map(s => `<option value="${s.name}">${s.name}</option>`).join('');
        if (currentValue) {
            select.value = currentValue;
        }
    } catch (error) {
        console.error('Error loading sites:', error);
    }
}

async function loadSettingsForAlert() {
    try {
        const data = await apiCall('/api/data');
        const settings = data.settings || {};
        
        document.getElementById('alertContactName').value = settings.default_contact_name || '';
        document.getElementById('alertContactPhone').value = settings.default_contact_phone || '';
        document.getElementById('alertContactEmail').value = settings.default_contact_email || '';
        document.getElementById('alertContactTeams').value = settings.default_contact_teams || '';
    } catch (error) {
        console.error('Error loading settings:', error);
    }
}

async function loadSettingsForTemplate() {
    try {
        const data = await apiCall('/api/data');
        const settings = data.settings || {};
        
        document.getElementById('templateContactName').value = settings.default_contact_name || '';
        document.getElementById('templateContactPhone').value = settings.default_contact_phone || '';
        document.getElementById('templateContactEmail').value = settings.default_contact_email || '';
        document.getElementById('templateContactTeams').value = settings.default_contact_teams || '';
    } catch (error) {
        console.error('Error loading settings:', error);
    }
}

function createModal(title, content) {
    const modal = document.createElement('div');
    modal.className = 'modal active';
    modal.innerHTML = `
        <div class="modal-content">
            <div class="modal-header">
                <h2>${title}</h2>
                <button class="close-btn" onclick="closeModal()">&times;</button>
            </div>
            ${content}
        </div>
    `;
    
    const container = document.getElementById('modalContainer');
    container.innerHTML = '';
    container.appendChild(modal);
    
    return modal;
}

function closeModal() {
    document.getElementById('modalContainer').innerHTML = '';
}

function showMessage(message, type) {
    const messageEl = document.createElement('div');
    messageEl.className = type;
    messageEl.textContent = message;
    messageEl.style.position = 'fixed';
    messageEl.style.top = '20px';
    messageEl.style.right = '20px';
    messageEl.style.zIndex = '10000';
    messageEl.style.minWidth = '300px';
    
    document.body.appendChild(messageEl);
    
    setTimeout(() => {
        messageEl.remove();
    }, 5000);
}


