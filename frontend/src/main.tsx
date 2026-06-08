import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import App from './App';
import './styles/global.css';
import { purgeStaleLocalStorage } from './pages/puzzle/utils';

// Run once at startup before the first render so stale data is gone before any component reads localStorage.
purgeStaleLocalStorage();

const root = document.getElementById('root');
if (!root) throw new Error('Missing #root element');

createRoot(root).render(
  <StrictMode>
    <BrowserRouter basename="/app">
      <App />
    </BrowserRouter>
  </StrictMode>
);
