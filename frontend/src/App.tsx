import { Routes, Route, useSearchParams } from 'react-router-dom';
import Layout from './components/Layout';
import IndexPage from './pages/IndexPage';
import AboutPage from './pages/AboutPage';
import ContactPage from './pages/ContactPage';
import PrivacyPolicyPage from './pages/PrivacyPolicyPage';
import LeaderboardPage from './pages/LeaderboardPage';
import CalendarPage from './pages/CalendarPage';
import ProfilePage from './pages/ProfilePage';
import AdminPage from './pages/AdminPage';
import PuzzlePage from './pages/PuzzlePage';
import PlayLandingPage from './pages/PlayLandingPage';

/** Forces a full remount of PuzzlePage when size or date changes, giving site.js a clean slate. */
function PuzzleRoute() {
  const [params] = useSearchParams();
  const size = params.get('size') ?? '17x17';
  const date = params.get('date') ?? '';
  return <PuzzlePage key={`${size}:${date}`} />;
}

export default function App() {
  return (
    <Layout>
      <Routes>
        <Route path="/" element={<IndexPage />} />
        <Route path="/play" element={<PlayLandingPage />} />
        <Route path="/puzzle" element={<PuzzleRoute />} />
        <Route path="/leaderboard" element={<LeaderboardPage />} />
        <Route path="/calendar" element={<CalendarPage />} />
        <Route path="/profile" element={<ProfilePage />} />
        <Route path="/admin" element={<AdminPage />} />
        <Route path="/about" element={<AboutPage />} />
        <Route path="/contact" element={<ContactPage />} />
        <Route path="/privacy-policy" element={<PrivacyPolicyPage />} />
      </Routes>
    </Layout>
  );
}
