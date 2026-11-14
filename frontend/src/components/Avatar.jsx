export default function Avatar({ photo, name, size = 44 }) {
  const style = { width: size, height: size, fontSize: size * 0.4 };
  if (photo) return <img className="avatar-img" src={photo} alt={name} style={style} />;
  return (
    <div className="avatar" style={style}>
      {(name || "C")[0].toUpperCase()}
    </div>
  );
}