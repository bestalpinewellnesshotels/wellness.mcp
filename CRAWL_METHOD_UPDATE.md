# Crawl Method Update - Status Tracking

Die Änderungen sind zu komplex für String-Replacement. Hier ist die neue Implementierung:

## UI-Anpassungen (prompts.html)

```javascript
var currentJobId = null;
var statusPollInterval = null;

function crawlSelectedHotels() {
  if (selectedHotels.size === 0) {
    toast('Bitte wähle mindestens ein Hotel aus', 'error');
    return;
  }
  
  var urls = Array.from(selectedHotels).map(function(hotelId) {
    var hotel = crawlData.find(function(h) { return h.hotelId === hotelId; });
    return hotel ? 'https://' + hotel.domain : null;
  }).filter(function(u) { return u !== null; });
  
  if (!confirm('Crawle ' + urls.length + '  Hotel(s)?\n\n' + urls.join('\n') + '\n\nDies kann mehrere Minuten dauern.')) {
    return;
  }
  
  showStatus('Starte Crawling', 'Initialisiere...', '');
  
  var apiBase = window.location.protocol + '//' + window.location.host + '/api/admin';
  
  fetch(apiBase + '/crawl-multiple-hotels', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      urls: urls,
      useSitemap: true
    })
  })
    .then(function(r) { return r.json(); })
    .then(function(data) {
      current JobId = data.jobId;
      updateStatus('Crawling läuft...', 'Job-ID: ' + data.jobId, 0);
      
      // Poll Status
      statusPollInterval = setInterval(function() {
        pollCrawlStatus(data.jobId);
      }, 2000);
    })
    .catch(function(e) {
      hideStatus();
      toast('Fehler beim Starten: ' + e.message, 'error');
    });
}

function pollCrawlStatus(jobId) {
  var apiBase = window.location.protocol + '//' + window.location.host + '/api/admin';
  
  fetch(apiBase + '/crawl/status/' + jobId)
    .then(function(r) { return r.json(); })
    .then(function(status) {
      if (status.status === 'completed') {
        clearInterval(statusPollInterval);
        updateStatus('Crawling abgeschlossen!', status.results.length + ' Hotels verarbeitet', 100);
        setTimeout(function() {
          hideStatus();
          var successful = status.results.filter(function(r) { return r.success; }).length;
          toast('Abgeschlossen: ' + successful + ' erfolgreich', 'success');
          loadCrawlData();
        }, 1500);
      } else {
        var progress = (status.processedHotels / status.totalHotels) * 100;
        var message = 'Hotel ' + (status.processedHotels + 1) + '/' + status.totalHotels + ': ' + (status.currentHotel || '...');
        var detail = 'Seite ' + status.currentHotelPages + (status.currentHotelTotalPages > 0 ? '/' + status.currentHotelTotalPages : '');
        updateStatus(message, detail, Math.floor(progress));
      }
    })
    .catch(function(e) {
      console.error('Poll error:', e);
    });
}
```

Die Backend-Änderungen sind zu groß. Ich empfehle manuelles Refactoring oder ich kann die komplette Methode neu erstellen.
