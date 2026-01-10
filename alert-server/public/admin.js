// Admin interface for GridBanner Alert Server
let apiKey = '';

// Handle login
async function handleLogin(event) {
    event.preventDefault();
    const password = document.getElementById('password').value;
    const errorEl = document.getElementById('errorMessage');
    
    try {
        // Try to authenticate by making a test API call
        const response = await fetch('/api/health');
        if (!response.ok) {
            throw new Error('Server error');
        }
        
        // Store the password as API key for subsequent requests
        apiKey = password;
        
        // Hide login, show main interface
        document.getElementById('loginContainer').style.display = 'none';
        document.getElementById('mainContainer').style.display = 'block';
        document.body.classList.remove('login-mode');
        document.body.classList.add('main-mode');
        
        // Load all data
        loadAllData();
    } catch (error) {
        errorEl.textContent = 'Invalid password. Please try again.';
        errorEl.classList.add('show');
        setTimeout(() => {
            errorEl.classList.remove('show');
        }, 3000);
    }
}

// API Helper
async function apiCall(endpoint, method = 'GET', body = null) {
    const url = `/api${endpoint}`;
    const options = {
        method,
        headers: {
            'Content-Type': 'application/json',
            'X-API-Key': apiKey
        }
    };

    if (body) {
        options.body = JSON.stringify(body);
    }

    const response = await fetch(url, options);
    
    if (!response.ok) {
        if (response.status === 401) {
            // Unauthorized - redirect to login
            document.getElementById('loginContainer').style.display = 'block';
            document.getElementById('mainContainer').style.display = 'none';
            document.body.classList.remove('main-mode');
            document.body.classList.add('login-mode');
            apiKey = '';
            throw new Error('Session expired. Please login again.');
        }
        const errorText = await response.text();
        throw new Error(`API Error: ${response.status} - ${errorText}`);
    }

    if (response.status === 204) {
        return null;
    }

    return await response.json();
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
    container.innerHTML = '<div class="empty-state">Loading alerts...</div>';

    try {
        const alert = await apiCall('/alert');
        
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
                        <button class="btn btn-danger" onclick="clearAlert()">Clear Alert</button>
                    </div>
                </div>
                <div class="item-details">
                    <div><strong>Level:</strong> ${level}</div>
                    <div><strong>Message:</strong> ${alert.message || 'N/A'}</div>
                    <div><strong>Background:</strong> ${alert.background_color || 'N/A'}</div>
                    <div><strong>Foreground:</strong> ${alert.foreground_color || 'N/A'}</div>
                    ${alert.site ? `<div><strong>Site:</strong> ${alert.site}</div>` : '<div><strong>Site:</strong> All Sites</div>'}
                    ${alert.audio_file ? `<div><strong>Sound:</strong> ${alert.audio_file}</div>` : '<div><strong>Sound:</strong> System Beep</div>'}
                    ${alert.alert_contact_name ? `<div><strong>Contact:</strong> ${alert.alert_contact_name}</div>` : ''}
                </div>
            </div>
        `;
    } catch (error) {
        container.innerHTML = `<div class="error show">Error loading alerts: ${error.message}</div>`;
    }
}

async function clearAlert() {
    if (!confirm('Are you sure you want to clear the active alert?')) {
        return;
    }

    try {
        await apiCall('/alert', 'DELETE');
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
                <label>Alert Sound</label>
                <select id="alertAudioFile">
                    <option value="">System Beep (default)</option>
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
                <button type="submit" class="btn">Create Alert</button>
                <button type="button" class="btn btn-secondary" onclick="closeModal()">Cancel</button>
            </div>
        </form>
    `);
    
    loadTemplatesForSelect('alertTemplate');
    loadSitesForSelect('alertSite');
    loadAudioFilesForSelect('alertAudioFile');
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
        audio_file: document.getElementById('alertAudioFile').value || null,
        alert_contact_name: document.getElementById('alertContactName').value || null,
        alert_contact_phone: document.getElementById('alertContactPhone').value || null,
        alert_contact_email: document.getElementById('alertContactEmail').value || null,
        alert_contact_teams: document.getElementById('alertContactTeams').value || null
    };

    try {
        await apiCall('/alert', 'POST', alert);
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
        const data = await apiCall('/data');
        const template = data.templates.find(t => t.id === templateId);
        if (template) {
            document.getElementById('alertLevel').value = template.level;
            document.getElementById('alertSummary').value = template.summary;
            document.getElementById('alertMessage').value = template.message;
            document.getElementById('alertBgColor').value = template.background_color;
            document.getElementById('alertFgColor').value = template.foreground_color;
            document.getElementById('alertSite').value = template.site || '';
            document.getElementById('alertAudioFile').value = template.audio_file || '';
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
    container.innerHTML = '<div class="empty-state">Loading templates...</div>';

    try {
        const data = await apiCall('/data');
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
                        <button class="btn" onclick="useTemplate('${template.id}')">Use</button>
                        <button class="btn btn-secondary" onclick="editTemplate('${template.id}')">Edit</button>
                        <button class="btn btn-danger" onclick="deleteTemplate('${template.id}')">Delete</button>
                    </div>
                </div>
                <div class="item-details">
                    <div><strong>Level:</strong> ${template.level}</div>
                    <div><strong>Summary:</strong> ${template.summary}</div>
                    <div><strong>Message:</strong> ${template.message}</div>
                    ${template.site ? `<div><strong>Site:</strong> ${template.site}</div>` : '<div><strong>Site:</strong> All Sites</div>'}
                    ${template.audio_file ? `<div><strong>Sound:</strong> ${template.audio_file}</div>` : '<div><strong>Sound:</strong> System Beep</div>'}
                </div>
            </div>
        `).join('');
    } catch (error) {
        container.innerHTML = `<div class="error show">Error loading templates: ${error.message}</div>`;
    }
}

async function useTemplate(templateId) {
    try {
        const data = await apiCall('/data');
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
            audio_file: template.audio_file || null,
            alert_contact_name: template.alert_contact_name || data.settings.default_contact_name,
            alert_contact_phone: template.alert_contact_phone || data.settings.default_contact_phone,
            alert_contact_email: template.alert_contact_email || data.settings.default_contact_email,
            alert_contact_teams: template.alert_contact_teams || data.settings.default_contact_teams
        };

        await apiCall('/alert', 'POST', alert);
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
                <label>Alert Sound</label>
                <select id="templateAudioFile">
                    <option value="">System Beep (default)</option>
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
                <button type="submit" class="btn">${isEdit ? 'Update' : 'Create'} Template</button>
                <button type="button" class="btn btn-secondary" onclick="closeModal()">Cancel</button>
            </div>
        </form>
    `);

    loadSitesForSelect('templateSite');
    loadAudioFilesForSelect('templateAudioFile');
    
    if (isEdit) {
        loadTemplateForEdit(templateId);
    } else {
        loadSettingsForTemplate();
    }
}

async function loadTemplateForEdit(templateId) {
    try {
        const data = await apiCall('/data');
        const template = data.templates.find(t => t.id === templateId);
        if (template) {
            document.getElementById('templateName').value = template.name;
            document.getElementById('templateLevel').value = template.level;
            document.getElementById('templateSummary').value = template.summary;
            document.getElementById('templateMessage').value = template.message;
            document.getElementById('templateBgColor').value = template.background_color;
            document.getElementById('templateFgColor').value = template.foreground_color;
            document.getElementById('templateSite').value = template.site || '';
            document.getElementById('templateAudioFile').value = template.audio_file || '';
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
        audio_file: document.getElementById('templateAudioFile').value || null,
        alert_contact_name: document.getElementById('templateContactName').value || null,
        alert_contact_phone: document.getElementById('templateContactPhone').value || null,
        alert_contact_email: document.getElementById('templateContactEmail').value || null,
        alert_contact_teams: document.getElementById('templateContactTeams').value || null
    };

    try {
        if (templateId) {
            await apiCall(`/templates/${templateId}`, 'PUT', template);
            showMessage('Template updated successfully', 'success');
        } else {
            await apiCall('/templates', 'POST', template);
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
        await apiCall(`/templates/${templateId}`, 'DELETE');
        showMessage('Template deleted successfully', 'success');
        loadTemplates();
    } catch (error) {
        showMessage(`Error deleting template: ${error.message}`, 'error');
    }
}

// Site Management
async function loadSites() {
    const container = document.getElementById('sitesList');
    container.innerHTML = '<div class="empty-state">Loading sites...</div>';

    try {
        const data = await apiCall('/data');
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
        container.innerHTML = `<div class="error show">Error loading sites: ${error.message}</div>`;
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
                <button type="submit" class="btn">${isEdit ? 'Update' : 'Create'} Site</button>
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
        const data = await apiCall('/data');
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
            await apiCall(`/sites/${siteId}`, 'PUT', site);
            showMessage('Site updated successfully', 'success');
        } else {
            await apiCall('/sites', 'POST', site);
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
        await apiCall(`/sites/${siteId}`, 'DELETE');
        showMessage('Site deleted successfully', 'success');
        loadSites();
    } catch (error) {
        showMessage(`Error deleting site: ${error.message}`, 'error');
    }
}

// System Management
async function loadSystems() {
    const container = document.getElementById('systemsList');
    container.innerHTML = '<div class="empty-state">Loading systems...</div>';

    try {
        const data = await apiCall('/data');
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
        container.innerHTML = `<div class="error show">Error loading systems: ${error.message}</div>`;
    }
}

// Settings Management
async function loadSettings() {
    try {
        const data = await apiCall('/data');
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
        await apiCall('/settings', 'PUT', settings);
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
    loadUsers();
    loadSettings();
}

async function loadTemplatesForSelect(selectId) {
    try {
        const data = await apiCall('/data');
        const select = document.getElementById(selectId);
        select.innerHTML = '<option value="">Select a template...</option>' +
            data.templates.map(t => `<option value="${t.id}">${t.name}</option>`).join('');
    } catch (error) {
        console.error('Error loading templates:', error);
    }
}

async function loadSitesForSelect(selectId) {
    try {
        const data = await apiCall('/data');
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
        const data = await apiCall('/data');
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
        const data = await apiCall('/data');
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
    messageEl.className = `error ${type === 'success' ? 'success' : ''}`;
    messageEl.style.background = type === 'success' ? '#dff6dd' : '#fee';
    messageEl.style.color = type === 'success' ? '#107c10' : '#c33';
    messageEl.textContent = message;
    messageEl.style.position = 'fixed';
    messageEl.style.top = '20px';
    messageEl.style.right = '20px';
    messageEl.style.zIndex = '10000';
    messageEl.style.minWidth = '300px';
    messageEl.style.padding = '12px';
    messageEl.style.borderRadius = '4px';
    messageEl.classList.add('show');
    
    document.body.appendChild(messageEl);
    
    setTimeout(() => {
        messageEl.remove();
    }, 5000);
}

// Audio File Management
async function loadAudioFiles() {
    const container = document.getElementById('audioList');
    container.innerHTML = '<div class="empty-state">Loading audio files...</div>';

    try {
        const files = await apiCall('/audio');
        
        if (files.length === 0) {
            container.innerHTML = '<div class="empty-state">No audio files found. Upload audio files to use custom alert sounds.</div>';
            return;
        }

        container.innerHTML = files.map(file => `
            <div class="alert-item" style="display: flex; justify-content: space-between; align-items: center;">
                <div class="item-title">${file.name}</div>
                <div class="item-actions">
                    <button class="btn btn-secondary" onclick="renameAudio('${file.name}')">Rename</button>
                    <button class="btn btn-danger" onclick="deleteAudio('${file.name}')">Delete</button>
                </div>
            </div>
        `).join('');
    } catch (error) {
        container.innerHTML = `<div class="error show">Error loading audio files: ${error.message}</div>`;
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
                <button type="submit" class="btn">Upload</button>
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
        const response = await fetch('/api/audio', {
            method: 'POST',
            headers: {
                'X-API-Key': apiKey
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
        await apiCall(`/audio/${encodeURIComponent(oldName)}`, 'PUT', { name: newName });
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
        await apiCall(`/audio/${encodeURIComponent(fileName)}`, 'DELETE');
        showMessage('Audio file deleted successfully', 'success');
        loadAudioFiles();
    } catch (error) {
        showMessage(`Error deleting audio: ${error.message}`, 'error');
    }
}

async function loadAudioFilesForSelect(selectId) {
    try {
        const files = await apiCall('/audio');
        const select = document.getElementById(selectId);
        const currentValue = select.value;
        select.innerHTML = '<option value="">System Beep (default)</option>' +
            files.map(f => `<option value="${f.name}">${f.name}</option>`).join('');
        if (currentValue) {
            select.value = currentValue;
        }
    } catch (error) {
        console.error('Error loading audio files:', error);
    }
}

// ============================================
// User Keyring Management
// ============================================

async function loadUsers() {
    const container = document.getElementById('usersList');
    if (!container) return;
    
    container.innerHTML = '<div class="loading">Loading users...</div>';
    
    try {
        const users = await apiCall('/users');
        
        if (users.length === 0) {
            container.innerHTML = `
                <div class="empty-state">
                    <p>No users have registered public keys yet.</p>
                    <p style="color: #666; font-size: 0.9em;">
                        Users can publish their SSH keys from GridBanner clients.
                    </p>
                </div>
            `;
            return;
        }
        
        container.innerHTML = users.map(user => `
            <div class="user-item" style="border: 1px solid #ddd; padding: 15px; margin-bottom: 10px; border-radius: 4px;">
                <div class="item-header" style="display: flex; justify-content: space-between; align-items: center;">
                    <div>
                        <div class="item-title" style="font-weight: bold; font-size: 1.1em;">${user.display_name || user.username}</div>
                        <div style="color: #666; font-size: 0.9em;">${user.username}</div>
                    </div>
                    <div class="item-actions">
                        <span class="badge" style="background: #00ADEE; color: white; padding: 4px 8px; border-radius: 4px;">
                            ${user.key_count} key${user.key_count !== 1 ? 's' : ''}
                        </span>
                        <button class="btn btn-secondary" onclick="showUserKeys('${encodeURIComponent(user.username)}')">View Keys</button>
                        <button class="btn btn-danger" onclick="deleteUser('${encodeURIComponent(user.username)}')">Delete</button>
                    </div>
                </div>
                ${user.last_seen ? `<div style="color: #999; font-size: 0.8em; margin-top: 5px;">Last seen: ${new Date(user.last_seen).toLocaleString()}</div>` : ''}
            </div>
        `).join('');
    } catch (error) {
        container.innerHTML = `<div class="error">Error loading users: ${error.message}</div>`;
    }
}

async function showUserKeys(encodedUsername) {
    const username = decodeURIComponent(encodedUsername);
    
    try {
        const keys = await apiCall(`/users/${encodedUsername}/keys`);
        
        const keysHtml = keys.length === 0 
            ? '<p style="color: #666;">No public keys registered.</p>'
            : keys.map(key => `
                <div style="border: 1px solid #eee; padding: 10px; margin-bottom: 10px; border-radius: 4px; background: #f9f9f9;">
                    <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px;">
                        <strong>${key.key_name || key.key_type}</strong>
                        <button class="btn btn-danger btn-sm" onclick="deleteUserKey('${encodedUsername}', '${key.id}')" style="padding: 4px 8px; font-size: 0.8em;">Delete</button>
                    </div>
                    <div style="font-size: 0.85em; color: #666;">
                        <div><strong>Type:</strong> ${key.key_type}</div>
                        ${key.fingerprint ? `<div><strong>Fingerprint:</strong> <code style="font-size: 0.85em;">${key.fingerprint}</code></div>` : ''}
                        <div><strong>Uploaded:</strong> ${new Date(key.uploaded_at).toLocaleString()}</div>
                    </div>
                    <div style="margin-top: 8px;">
                        <textarea readonly style="width: 100%; height: 60px; font-family: monospace; font-size: 0.75em; resize: vertical;">${key.key_data}</textarea>
                    </div>
                </div>
            `).join('');
        
        const modal = createModal(`Public Keys for ${username}`, `
            <div style="max-height: 60vh; overflow-y: auto;">
                ${keysHtml}
            </div>
            <div style="margin-top: 15px; text-align: right;">
                <button class="btn btn-secondary" onclick="closeModal()">Close</button>
            </div>
        `);
    } catch (error) {
        showMessage(`Error loading keys: ${error.message}`, 'error');
    }
}

async function deleteUserKey(encodedUsername, keyId) {
    if (!confirm('Are you sure you want to delete this key?')) {
        return;
    }
    
    try {
        await apiCall(`/users/${encodedUsername}/keys/${keyId}`, 'DELETE');
        showMessage('Key deleted successfully', 'success');
        closeModal();
        showUserKeys(encodedUsername);
    } catch (error) {
        showMessage(`Error deleting key: ${error.message}`, 'error');
    }
}

async function deleteUser(encodedUsername) {
    const username = decodeURIComponent(encodedUsername);
    if (!confirm(`Are you sure you want to delete user "${username}" and all their keys?`)) {
        return;
    }
    
    try {
        await apiCall(`/users/${encodedUsername}`, 'DELETE');
        showMessage('User deleted successfully', 'success');
        loadUsers();
    } catch (error) {
        showMessage(`Error deleting user: ${error.message}`, 'error');
    }
}

